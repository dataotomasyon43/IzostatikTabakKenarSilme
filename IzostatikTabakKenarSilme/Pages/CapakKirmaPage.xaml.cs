using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IzostatikTabakKenarSilme.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// AYARLAR > Çapak Kırma (istasyon 2, Bomse 2).
    ///
    /// Okuma (yalnız sayfa açıkken): parametreler, bomse ve modül aktüel
    /// pozisyonları, "geri" yön biti.
    /// Yazma (yalnız operatör eylemiyle):
    ///   - Parametre kutusunda onaylanan değer → ilgili tag.
    ///   - TEACH, Bomse İleri/Geri Dön, Modül İleri/Geri: basılı tuttukça TRUE.
    ///   - GERİ (HMI_Bomse2Yon): kalıcı.
    /// Ek koşullar PLC tarafında.
    /// </summary>
    public partial class CapakKirmaPage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        // Ayar satırı ↔ tag (INT ya da REAL).
        private sealed class Param
        {
            public ParameterField Field;
            public string Tag;
            public bool IsInt;
        }

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;

        private readonly List<Param> _params;
        private readonly List<PlcPushButton> _pushes;
        private readonly HashSet<ParameterField> _writingFields = new HashSet<ParameterField>();

        private bool? _readOk;

        public CapakKirmaPage(PlcService plc, PlcCommands commands, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent();

            _params = new List<Param>
            {
                new Param { Field = BomseBasPozField, Tag = PlcTags.CpkBomseBasPoz },
                new Param { Field = BitisAciField, Tag = PlcTags.CpkBitisAci },
                new Param { Field = GeriCekilmeAciField, Tag = PlcTags.CpkModGeriCekilmeAci },
                new Param { Field = TurSureField, Tag = PlcTags.CpkTurSure },
                new Param { Field = TurSayiField, Tag = PlcTags.CpkTurSayi, IsInt = true },
                new Param { Field = ModHizField, Tag = PlcTags.CpkModHiz, IsInt = true },
                new Param { Field = ModIvmeField, Tag = PlcTags.CpkModIvme, IsInt = true },
                new Param { Field = ModBasPozField, Tag = PlcTags.CpkModBasPoz }
            };

            foreach (Param p in _params)
            {
                Param param = p;
                param.Field.ValueEntered += (s, v) => WriteParameter(param, v);
            }

            TeachButton.PlcTag = PlcTags.CpkTeach;
            BomseIleriButton.PlcTag = PlcTags.CpkBomseIleri;
            BomseGeriButton.PlcTag = PlcTags.CpkBomseGeri;
            ModulIleriButton.PlcTag = PlcTags.CpkModulIleri;
            ModulGeriButton.PlcTag = PlcTags.CpkModulGeri;

            _pushes = new List<PlcPushButton>
            {
                TeachButton, BomseIleriButton, BomseGeriButton, ModulIleriButton, ModulGeriButton
            };

            foreach (PlcPushButton b in _pushes)
            {
                b.Commands = _commands;
                b.CommandFailed += (s, m) => ShowStatus(m, isError: true);
            }

            GeriYonButton.PlcTag = PlcTags.CpkBomseGeriYon;
            GeriYonButton.Commands = _commands;
            GeriYonButton.CommandFailed += (s, m) => ShowStatus(m, isError: true);

            ShowUnknown();

            new PagePoller(this, PollInterval, PollAsync, OnReadError);
        }

        // ---------------- Okuma ----------------

        private async Task PollAsync(CancellationToken ct)
        {
            if (!_state.IsConnected)
            {
                ShowUnknown();
                return;
            }

            var paramTags = _params.Select(p => new { p.Tag, p.IsInt }).ToList();

            var d = await Task.Run(() => new
            {
                Params = paramTags
                    .Select(p => p.IsInt ? (double)_plc.ReadIntTag(p.Tag) : _plc.ReadRealTag(p.Tag))
                    .ToList(),
                BomseAkt = _plc.ReadRealTag(PlcTags.CpkBomseAktPoz),
                ModAkt = _plc.ReadRealTag(PlcTags.CpkModAktPoz),
                GeriYon = _plc.ReadBoolTag(PlcTags.CpkBomseGeriYon)
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            for (int i = 0; i < _params.Count; i++)
                ShowField(_params[i].Field, d.Params[i]);

            BomseAktPozField.SetValue(d.BomseAkt);
            ModAktPozField.SetValue(d.ModAkt);
            GeriYonButton.SetState(d.GeriYon);

            SetPushEnabled(true);

            UpdateTurSureHint();

            if (_readOk == false)
                ShowStatus(string.Empty, isError: false);

            _readOk = true;
        }

        // ---------------- Tur süresi önerisi ----------------

        // PLC'deki forma göre önerilen en kısa tur süresi; yalnız tavsiye,
        // girilen değer sınırlanmaz. Öneri arka planda bir kez hesaplanır
        // (form değişince yeniden).
        private readonly Services.Tabak.TabakLibrary _formLibrary = new Services.Tabak.TabakLibrary();
        private Services.Tabak.TurSuresiOnerisi.Sonuc _turOneri;
        private DateTime _turOneriTime;

        private async void UpdateTurSureHint()
        {
            try
            {
                if ((DateTime.Now - _turOneriTime).TotalSeconds > 10)
                {
                    _turOneriTime = DateTime.Now;
                    var lib = _formLibrary;
                    _turOneri = await System.Threading.Tasks.Task.Run(() => Services.Tabak.TurSuresiOnerisi.Hesapla(lib));
                }

                if (_turOneri == null)
                {
                    TurSureHintText.Visibility = Visibility.Collapsed;
                    return;
                }

                double oneri = _turOneri.OnerilenSn;
                string txt = oneri.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
                bool kisa = TurSureField.Value.HasValue && TurSureField.Value.Value < oneri - 0.05;

                TurSureHintText.Text = kisa
                    ? $"⚠ Önerilen en az {txt} s ({_turOneri.FormAdi}). Daha kısa sürede modül köşelerde zorlanır."
                    : $"Önerilen en az {txt} s ({_turOneri.FormAdi})";
                TurSureHintText.SetResourceReference(TextBlock.ForegroundProperty, kisa ? "WarningColor" : "SecondaryText");
                TurSureHintText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Tur süresi önerisi gösterilemedi: " + ex.Message);
            }
        }

        private void OnReadError(Exception ex)
        {
            ShowUnknown();

            if (_readOk != false)
            {
                _readOk = false;
                AppLogger.Warn($"Çapak Kırma sayfası: okunamadı: {ex.Message}");
                ShowStatus("Değerler okunamadı: " + ex.Message, isError: true);
            }
        }

        private void ShowUnknown()
        {
            foreach (Param p in _params)
                ShowField(p.Field, null);

            BomseAktPozField.SetValue(null);
            ModAktPozField.SetValue(null);
            GeriYonButton.SetState(null);

            // Basılıysa HoldButton "bırakıldı" verir (FALSE yazılır).
            SetPushEnabled(false);
        }

        private void SetPushEnabled(bool enabled)
        {
            foreach (PlcPushButton b in _pushes)
                b.IsEnabled = enabled;
        }

        private void ShowField(ParameterField field, double? value)
        {
            if (!_writingFields.Contains(field))
                field.SetValue(value);
        }

        // ---------------- Parametre yazma ----------------

        private async void WriteParameter(Param param, double value)
        {
            ParameterField field = param.Field;
            string unit = string.IsNullOrEmpty(field.Unit) ? string.Empty : " " + field.Unit;
            string oldText = field.Value.HasValue ? field.Value.Value.ToString("F" + field.Decimals, Tr) : "—";
            string newText = value.ToString("F" + field.Decimals, Tr);
            string description = $"Çapak Kırma: {field.Label} {oldText} → {newText}{unit}";

            _writingFields.Add(field);
            field.SetBusy(true);

            try
            {
                if (param.IsInt)
                    await _commands.WriteIntAsync(param.Tag, (short)value);
                else
                    await _commands.WriteRealAsync(param.Tag, (float)value);

                AppLogger.Info($"Parametre yazıldı: {description}");
                ShowStatus($"Kaydedildi: {field.Label} = {newText}{unit}", isError: false);
                field.SetValue(value);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Parametre yazılamadı: {description}", ex);
                ShowStatus($"Yazılamadı ({field.Label}): {ex.Message}", isError: true);
            }
            finally
            {
                _writingFields.Remove(field);
                field.SetBusy(false);
            }
        }

        // ---------------- Durum satırı ----------------

        private void ShowStatus(string message, bool isError)
        {
            PageStatusText.Text = string.IsNullOrEmpty(message)
                ? string.Empty
                : $"{DateTime.Now:HH:mm:ss}  {message}";

            PageStatusText.SetResourceReference(
                TextBlock.ForegroundProperty,
                isError ? "ErrorColor" : "SecondaryText");
        }
    }
}
