using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace IzostatikTabakKenarSilme.Services.Tabak
{
    /// <summary>
    /// Tabak profilini PLC'ye yükler (ReceteTabakForm protokolü).
    ///
    /// PLC tarafı (Sysmac, ReceteTabakForm):
    ///   rung 0: HMI_Recete_Yukle ↑ → CamNode'ları Rec1..6'dan doldur,
    ///           Recete_Aktar := TRUE
    ///   rung 1: Recete_Aktar + 500 ms → MC_GenerateCamTable → Done →
    ///           MC_SaveCamTable → Done: HMI_Recete_Yuklendi S, Recete_Aktar R
    ///   rung 2: HMI_Recete_Yukle ↑ → HMI_Recete_Yuklendi R
    ///   rung 3: HMI_Recete_Yuklendi → HMI_Recete_Yukle R
    ///
    /// Akış:
    ///   1. Koşullar: PLC bağlı, mod Güvenli/Manuel, AutoCycle kapalı.
    ///   2. Modül başlama pozisyonları okunur, 400-600 mm kontrolü.
    ///   3. HMI_Recete_Yukle TRUE kalmışsa FALSE yazılır.
    ///   4. Rec1..Rec6 yazılır ve geri okunarak karşılaştırılır.
    ///   5. HMI_Recete_Yukle := TRUE.
    ///   6. HMI_Recete_Yuklendi önce FALSE (istek alındı), sonra TRUE
    ///      (cam tablosu üretildi ve kaydedildi) beklenir.
    ///   7. Zaman aşımında HMI_Recete_Yukle FALSE yazılır; sonuç asla
    ///      başarılı sayılmaz.
    /// Hiçbir hareket / start / motor biti yazılmaz.
    /// PLC'nin cam hata çıkışları (mcerror) program içi değişken olduğu
    /// için okunamaz; hata ancak zaman aşımıyla anlaşılır.
    /// </summary>
    public sealed class TabakUploader
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan DoneTimeout = TimeSpan.FromSeconds(15);

        // Geri okumada kabul edilen fark (Rec birimi: 0,1 µm).
        private const float ReadBackTolerance = 1.0f;

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;

        public TabakUploader(PlcService plc, PlcCommands commands, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public sealed class Result
        {
            public bool Success;
            public string Message;
            public List<TabakRecipe.ModuleCheck> Modules = new List<TabakRecipe.ModuleCheck>();
        }

        /// <summary>
        /// Yükleme koşulları; uygunsa null, değilse nedeni.
        /// UI thread'inden çağrılmalı (MachineState).
        /// </summary>
        public string CheckPreconditions()
        {
            if (!_state.IsConnected)
                return "PLC bağlantısı yok.";

            if (_state.AutoCycle == true)
                return "Makine çalışıyor (AutoCycle). Önce durdurun.";

            if (_state.Mode != MachineModes.Guvenli && _state.Mode != MachineModes.Manuel)
                return "Yükleme için makine Güvenli ya da Manuel modda olmalı.";

            return null;
        }

        /// <summary>Modül başlama pozisyonlarını okuyup sınır kontrolü yapar.</summary>
        public async Task<List<TabakRecipe.ModuleCheck>> CheckModulesAsync(TabakProfile profile)
        {
            List<double> starts = await Task.Run(() =>
                TabakRecipe.Modules.Select(m => (double)_plc.ReadRealTag(m.StartPosTag)).ToList());

            return TabakRecipe.Modules
                .Select((m, i) => TabakRecipe.Check(profile, m.Name, starts[i]))
                .ToList();
        }

        /// <summary>UI thread'inden çağrılır; progress UI thread'ine raporlar.</summary>
        public async Task<Result> UploadAsync(TabakProfile profile, string formName, IProgress<string> progress)
        {
            var result = new Result();

            string block = CheckPreconditions();
            if (block != null)
                return Fail(result, block);

            if (profile == null || !profile.IsValid)
                return Fail(result, "Profil geçersiz; yükleme yapılmadı.");

            // 1) Mekanik sınırlar (güncel başlama pozisyonlarıyla)
            progress?.Report("Modül başlama pozisyonları okunuyor...");
            result.Modules = await CheckModulesAsync(profile);

            var bad = result.Modules.Where(m => !m.Ok).ToList();
            if (bad.Count > 0)
            {
                return Fail(result, "Mekanik sınır aşılıyor (400-600 mm): " +
                    string.Join("; ", bad.Select(m => $"{m.Name}: {m.ViolationText(3)}")));
            }

            // 2) Diziler
            float[][] rec = TabakRecipe.ToRecArrays(profile);

            bool requestSent = false;

            try
            {
                // 3) Önceki istek takılı kaldıysa temizle (yükselen kenar için)
                bool yukle = await Task.Run(() => _plc.ReadBoolTag(PlcTags.ReceteYukle));
                if (yukle)
                {
                    progress?.Report("Önceki yükleme isteği temizleniyor...");
                    await _commands.SetBoolAsync(PlcTags.ReceteYukle, false);
                    await Task.Delay(300);
                }

                // 4) Rec1..Rec6 yaz + geri oku
                for (int a = 0; a < rec.Length; a++)
                {
                    string tag = PlcTags.RecArrays[a];
                    progress?.Report($"{tag} yazılıyor ({a + 1}/{rec.Length})...");
                    await _commands.WriteRealArrayAsync(tag, rec[a]);
                }

                progress?.Report("Yazılan değerler doğrulanıyor...");

                for (int a = 0; a < rec.Length; a++)
                {
                    string tag = PlcTags.RecArrays[a];
                    float[] back = await Task.Run(() => _plc.ReadRealArray(tag, PlcTags.RecLength));

                    for (int j = 0; j < PlcTags.RecLength; j++)
                    {
                        if (Math.Abs(back[j] - rec[a][j]) > ReadBackTolerance)
                        {
                            return Fail(result, $"Doğrulama hatası: {tag}[{j}] yazılan {rec[a][j]:0}, okunan {back[j]:0}. " +
                                                "Yükleme isteği gönderilmedi.");
                        }
                    }
                }

                // Koşullar yazma sırasında değişmiş olabilir
                block = CheckPreconditions();
                if (block != null)
                    return Fail(result, block + " Yükleme isteği gönderilmedi.");

                // 5) Yükleme isteği
                progress?.Report("PLC cam tablosunu oluşturuyor...");
                await _commands.SetBoolAsync(PlcTags.ReceteYukle, true);
                requestSent = true;

                // 6a) İstek alındı: Yuklendi FALSE olmalı (rung 2)
                if (!await WaitForAsync(PlcTags.ReceteYuklendi, false, AckTimeout))
                    return await FailAndClearAsync(result, "PLC yükleme isteğini almadı (Yüklendi biti sıfırlanmadı).");

                // 6b) Cam tablosu üretildi + kaydedildi: Yuklendi TRUE
                if (!await WaitForAsync(PlcTags.ReceteYuklendi, true, DoneTimeout))
                {
                    return await FailAndClearAsync(result,
                        $"PLC {DoneTimeout.TotalSeconds:0} sn içinde onay vermedi. Cam tablosu oluşturulamamış olabilir; " +
                        "yükleme BAŞARISIZ kabul edildi.");
                }

                // PLC isteği kendisi sıfırlar (rung 3); sıfırlamadıysa temizle.
                if (!await WaitForAsync(PlcTags.ReceteYukle, false, TimeSpan.FromSeconds(1)))
                    await _commands.SetBoolAsync(PlcTags.ReceteYukle, false);

                result.Success = true;
                result.Message = $"{formName} PLC'ye yüklendi (Rmin {profile.RMin:0.00} mm, " +
                                 $"Rmax {profile.RMax:0.00} mm, kurs {profile.RMax - profile.RMin:0.00} mm).";
                AppLogger.Info("Tabak yüklendi: " + result.Message);
                return result;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Tabak yükleme hatası ({formName})", ex);

                if (requestSent)
                    return await FailAndClearAsync(result, "Haberleşme hatası: " + ex.Message);

                return Fail(result, "Haberleşme hatası: " + ex.Message);
            }
        }

        private async Task<bool> WaitForAsync(string tag, bool expected, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();

            while (sw.Elapsed < timeout)
            {
                bool value = await Task.Run(() => _plc.ReadBoolTag(tag));
                if (value == expected)
                    return true;

                await Task.Delay(PollInterval);
            }

            return false;
        }

        private async Task<Result> FailAndClearAsync(Result result, string message)
        {
            try
            {
                await _commands.SetBoolAsync(PlcTags.ReceteYukle, false);
            }
            catch (Exception ex)
            {
                AppLogger.Error("HMI_Recete_Yukle sıfırlanamadı", ex);
                message += " (Yükleme isteği sıfırlanamadı.)";
            }

            return Fail(result, message);
        }

        private static Result Fail(Result result, string message)
        {
            result.Success = false;
            result.Message = message;
            AppLogger.Warn("Tabak yüklenemedi: " + message);
            return result;
        }
    }
}
