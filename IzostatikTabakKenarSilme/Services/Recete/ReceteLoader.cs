using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IzostatikTabakKenarSilme.Services.Tabak;

namespace IzostatikTabakKenarSilme.Services.Recete
{
    /// <summary>
    /// Reçete okuma (PLC'deki güncel değerler) ve reçeteyi PLC'ye yükleme.
    ///
    /// Yükleme sırası:
    ///   1. Koşullar: PLC bağlı, Güvenli/Manuel mod, AutoCycle kapalı.
    ///   2. Tüm değerler sınır içinde mi (değer kırpılmaz; dışarıdaysa durur).
    ///   3. Reçetede tabak formu varsa profil hesaplanır ve reçetedeki modül
    ///      başlama pozisyonlarıyla 400-600 mm kontrolü yapılır.
    ///   4. Ayarlar sırayla yazılır, sonra geri okunup karşılaştırılır.
    ///   5. Tabak formu TabakUploader ile yüklenir (Rec1..6 + el sıkışma).
    /// Hiçbir hareket / start / motor biti yazılmaz.
    /// </summary>
    public sealed class ReceteLoader
    {
        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly TabakUploader _tabak;

        public ReceteLoader(PlcService plc, PlcCommands commands, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _tabak = new TabakUploader(plc, commands, state);
        }

        public sealed class Result
        {
            public bool Success;
            public string Message;
            public int Written;
        }

        /// <summary>Uygunsa null, değilse nedeni (UI thread'inden).</summary>
        public string CheckPreconditions()
        {
            return _tabak.CheckPreconditions();
        }

        /// <summary>Reçetedeki tüm ayarların PLC'deki güncel değerleri.</summary>
        public Task<Dictionary<string, double>> ReadAllAsync()
        {
            return Task.Run(() =>
            {
                var values = new Dictionary<string, double>();

                foreach (ReceteParam p in ReceteParams.All)
                    values[p.Tag] = Read(p);

                return values;
            });
        }

        private double Read(ReceteParam p)
        {
            switch (p.Type)
            {
                case ReceteParamType.Bool: return _plc.ReadBoolTag(p.Tag) ? 1 : 0;
                case ReceteParamType.Int: return _plc.ReadIntTag(p.Tag);
                default: return _plc.ReadRealTag(p.Tag);
            }
        }

        private Task Write(ReceteParam p, double v)
        {
            switch (p.Type)
            {
                case ReceteParamType.Bool: return _commands.SetBoolAsync(p.Tag, v != 0);
                case ReceteParamType.Int: return _commands.WriteIntAsync(p.Tag, (short)Math.Round(v));
                default: return _commands.WriteRealAsync(p.Tag, (float)v);
            }
        }

        /// <summary>
        /// Reçetedeki değerlerin sınır dışı olanları (boş liste: hepsi uygun).
        /// Reçetede olmayan ayarlar da listelenir.
        /// </summary>
        public static List<string> Validate(ReceteData r)
        {
            var errors = new List<string>();

            foreach (ReceteParam p in ReceteParams.All)
            {
                if (!r.Values.TryGetValue(p.Tag, out double v))
                    errors.Add($"{p.Group} / {p.Label}: reçetede yok");
                else if (!p.InRange(v))
                    errors.Add($"{p.Group} / {p.Label}: {p.Format(v)} sınır dışı");
            }

            return errors;
        }

        /// <summary>Reçetenin tabak formu profili (form yoksa null).</summary>
        public static TabakProfile ComputeProfile(string formPath)
        {
            if (formPath == null)
                return null;

            ExcelPointReader.Result read = ExcelPointReader.Read(formPath);
            return TabakProfile.Compute(read.Points);
        }

        /// <summary>Reçetedeki başlama pozisyonlarıyla modül sınır kontrolü.</summary>
        public static List<TabakRecipe.ModuleCheck> CheckModules(ReceteData r, TabakProfile profile)
        {
            var list = new List<TabakRecipe.ModuleCheck>();

            foreach (TabakRecipe.Module m in TabakRecipe.Modules)
            {
                if (r.Values.TryGetValue(m.StartPosTag, out double start))
                    list.Add(TabakRecipe.Check(profile, m.Name, start));
            }

            return list;
        }

        /// <summary>UI thread'inden çağrılır.</summary>
        public async Task<Result> LoadAsync(ReceteData r, string formPath, IProgress<string> progress)
        {
            var result = new Result();

            string block = CheckPreconditions();
            if (block != null)
                return Fail(result, block);

            // Sınırlar
            List<string> errors = Validate(r);
            if (errors.Count > 0)
            {
                return Fail(result, "Reçete yüklenmedi, geçersiz değer: " +
                    string.Join("; ", errors.Take(4)) + (errors.Count > 4 ? $" … (+{errors.Count - 4})" : ""));
            }

            // Tabak formu ve mekanik sınır (yazmadan önce)
            TabakProfile profile = null;

            if (formPath != null)
            {
                progress?.Report("Tabak formu hesaplanıyor...");

                try
                {
                    profile = await Task.Run(() => ComputeProfile(formPath));
                }
                catch (Exception ex)
                {
                    return Fail(result, "Reçetenin tabak formu okunamadı: " + ex.Message);
                }

                if (!profile.IsValid)
                    return Fail(result, "Reçetenin tabak formu geçersiz: " + string.Join(" ", profile.Errors));

                var bad = CheckModules(r, profile).Where(m => !m.Ok).ToList();
                if (bad.Count > 0)
                {
                    return Fail(result, "Mekanik sınır aşılıyor (400-600 mm): " +
                        string.Join("; ", bad.Select(m => $"{m.Name}: {m.ViolationText(3)}")));
                }
            }

            // Ayarları yaz
            List<ReceteParam> ps = ReceteParams.All.ToList();

            try
            {
                for (int i = 0; i < ps.Count; i++)
                {
                    ReceteParam p = ps[i];
                    progress?.Report($"Ayarlar yazılıyor ({i + 1}/{ps.Count}): {p.Group} / {p.Label}");
                    await Write(p, r.Values[p.Tag]);
                    result.Written++;
                }

                progress?.Report("Yazılan ayarlar doğrulanıyor...");
                Dictionary<string, double> back = await ReadAllAsync();

                var diff = ps.Where(p => !p.SameValue(back[p.Tag], r.Values[p.Tag])).ToList();
                if (diff.Count > 0)
                {
                    return Fail(result, "Doğrulama hatası: " + string.Join("; ", diff.Take(4)
                        .Select(p => $"{p.Group} / {p.Label} yazılan {p.Format(r.Values[p.Tag])}, okunan {p.Format(back[p.Tag])}")) +
                        (formPath != null ? ". Tabak formu yüklenmedi." : ""));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Reçete yazma hatası ({r.Name})", ex);
                return Fail(result, $"Haberleşme hatası ({result.Written}/{ps.Count} ayar yazıldı): {ex.Message}" +
                    (formPath != null ? " Tabak formu yüklenmedi." : ""));
            }

            AppLogger.Info($"Reçete ayarları yazıldı: {r.Name} ({ps.Count} ayar)");

            // Tabak formu
            if (profile != null)
            {
                TabakUploader.Result t = await _tabak.UploadAsync(profile, r.FormName ?? r.Name, progress);

                if (!t.Success)
                    return Fail(result, $"Ayarlar yazıldı ({ps.Count}) ama tabak formu yüklenemedi: {t.Message}");
            }

            result.Success = true;
            result.Message = profile != null
                ? $"\"{r.Name}\" yüklendi: {ps.Count} ayar + tabak formu \"{r.FormName}\"."
                : $"\"{r.Name}\" yüklendi: {ps.Count} ayar (reçetede tabak formu yok).";
            AppLogger.Info("Reçete yüklendi: " + result.Message);
            return result;
        }

        private static Result Fail(Result result, string message)
        {
            result.Success = false;
            result.Message = message;
            AppLogger.Warn("Reçete yüklenemedi: " + message);
            return result;
        }
    }
}
