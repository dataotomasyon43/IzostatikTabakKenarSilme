using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using IzostatikTabakKenarSilme.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// AYARLAR > Giriş Çıkış Bomse (giriş = Bomse 1, çıkış = Bomse 8).
    ///
    /// Okuma (yalnız sayfa açıkken): iki offset, iki aktüel pozisyon.
    /// Yazma (yalnız operatör eylemiyle):
    ///   - Offset (0-360 °) → HMI_BomIn_Alma_Pos / HMI_BomOut_Alma_Pos.
    ///   - İleri / Geri Dön: basılı tuttukça JogP / JogN = TRUE.
    /// Ek koşullar PLC tarafında.
    /// </summary>
    public partial class GirisCikisBomsePage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;

        private readonly List<PlcPushButton> _pushes;
        private readonly HashSet<ParameterField> _writingFields = new HashSet<ParameterField>();

        private bool? _readOk;

        public GirisCikisBomsePage(PlcService plc, PlcCommands commands, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent();

            GirisOffsetField.ValueEntered += (s, v) =>
                WriteOffset(GirisOffsetField, PlcTags.GirisBomseOffset, "Giriş Bomse", v);
            CikisOffsetField.ValueEntered += (s, v) =>
                WriteOffset(CikisOffsetField, PlcTags.CikisBomseOffset, "Çıkış Bomse", v);

            GirisIleriButton.PlcTag = PlcTags.GirisBomseIleri;
            GirisGeriButton.PlcTag = PlcTags.GirisBomseGeri;
            CikisIleriButton.PlcTag = PlcTags.CikisBomseIleri;
            CikisGeriButton.PlcTag = PlcTags.CikisBomseGeri;

            _pushes = new List<PlcPushButton>
            {
                GirisIleriButton, GirisGeriButton, CikisIleriButton, CikisGeriButton
            };

            foreach (PlcPushButton b in _pushes)
            {
                b.Commands = _commands;
                b.CommandFailed += (s, m) => ShowStatus(m, isError: true);
            }

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

            var d = await Task.Run(() => new
            {
                GirisOffset = _plc.ReadRealTag(PlcTags.GirisBomseOffset),
                CikisOffset = _plc.ReadRealTag(PlcTags.CikisBomseOffset),
                GirisAkt = _plc.ReadRealTag(PlcTags.GirisBomseAktPoz),
                CikisAkt = _plc.ReadRealTag(PlcTags.CikisBomseAktPoz)
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            ShowField(GirisOffsetField, d.GirisOffset);
            ShowField(CikisOffsetField, d.CikisOffset);
            GirisAktPozField.SetValue(d.GirisAkt);
            CikisAktPozField.SetValue(d.CikisAkt);

            SetPushEnabled(true);

            if (_readOk == false)
                ShowStatus(string.Empty, isError: false);

            _readOk = true;
        }

        private void OnReadError(Exception ex)
        {
            ShowUnknown();

            if (_readOk != false)
            {
                _readOk = false;
                AppLogger.Warn($"Giriş Çıkış Bomse sayfası: okunamadı: {ex.Message}");
                ShowStatus("Değerler okunamadı: " + ex.Message, isError: true);
            }
        }

        private void ShowUnknown()
        {
            ShowField(GirisOffsetField, null);
            ShowField(CikisOffsetField, null);
            GirisAktPozField.SetValue(null);
            CikisAktPozField.SetValue(null);

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

        // ---------------- Offset yazma ----------------

        private async void WriteOffset(ParameterField field, string tag, string station, double value)
        {
            string oldText = field.Value.HasValue ? field.Value.Value.ToString("F" + field.Decimals, Tr) : "—";
            string newText = value.ToString("F" + field.Decimals, Tr);
            string description = $"{station}: {field.Label} {oldText} → {newText} °";

            _writingFields.Add(field);
            field.SetBusy(true);

            try
            {
                await _commands.WriteRealAsync(tag, (float)value);

                AppLogger.Info($"Parametre yazıldı: {description}");
                ShowStatus($"Kaydedildi: {station} {field.Label} = {newText} °", isError: false);
                field.SetValue(value);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Parametre yazılamadı: {description}", ex);
                ShowStatus($"Yazılamadı ({station} {field.Label}): {ex.Message}", isError: true);
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
