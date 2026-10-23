using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Shapes;
using IzostatikTabakKenarSilme.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// AYARLAR > Döner Tabla.
    ///
    /// Okuma (yalnız sayfa açıkken): göbek hızı, tabla yerinde, baskı
    /// butonlarının durumu.
    /// Yazma (yalnız operatör eylemiyle):
    ///   - Göbek Hızı (0-99 %) → HMI_Doner_Tabla_Hiz.
    ///   - GÖBEK ADIM: basılı tuttukça TRUE, bırakınca FALSE.
    ///   - Baskı-1/2/3 Aşağı: kalıcı.
    /// Ek koşullar PLC tarafında.
    /// </summary>
    public partial class DonerTablaPage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;

        private readonly List<PlcToggleButton> _toggles;

        private bool _writingHiz;
        private bool? _readOk;

        public DonerTablaPage(PlcService plc, PlcCommands commands, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent();

            GobekHizField.ValueEntered += (s, v) => WriteGobekHiz(v);

            GobekAdimButton.PlcTag = PlcTags.GobekAdim;
            GobekAdimButton.Commands = _commands;
            GobekAdimButton.CommandFailed += (s, m) => ShowStatus(m, isError: true);

            Baski1Button.PlcTag = PlcTags.Baski1Asagi;
            Baski2Button.PlcTag = PlcTags.Baski2Asagi;
            Baski3Button.PlcTag = PlcTags.Baski3Asagi;

            _toggles = new List<PlcToggleButton> { Baski1Button, Baski2Button, Baski3Button };

            foreach (PlcToggleButton t in _toggles)
            {
                t.Commands = _commands;
                t.CommandFailed += (s, m) => ShowStatus(m, isError: true);
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
                Hiz = _plc.ReadIntTag(PlcTags.GobekHiz),
                Yerinde = _plc.ReadBoolTag(PlcTags.TablaYerinde1),
                Baski1 = _plc.ReadBoolTag(PlcTags.Baski1Asagi),
                Baski2 = _plc.ReadBoolTag(PlcTags.Baski2Asagi),
                Baski3 = _plc.ReadBoolTag(PlcTags.Baski3Asagi)
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            if (!_writingHiz)
                GobekHizField.SetValue(d.Hiz);

            SetLamp(TablaYerindeLamp, d.Yerinde);

            Baski1Button.SetState(d.Baski1);
            Baski2Button.SetState(d.Baski2);
            Baski3Button.SetState(d.Baski3);

            GobekAdimButton.IsEnabled = true;

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
                AppLogger.Warn($"Döner Tabla sayfası: okunamadı: {ex.Message}");
                ShowStatus("Değerler okunamadı: " + ex.Message, isError: true);
            }
        }

        private void ShowUnknown()
        {
            if (!_writingHiz)
                GobekHizField.SetValue(null);

            SetLamp(TablaYerindeLamp, null);

            foreach (PlcToggleButton t in _toggles)
                t.SetState(null);

            // Basılıysa HoldButton "bırakıldı" verir (FALSE yazılır).
            GobekAdimButton.IsEnabled = false;
        }

        private static void SetLamp(Ellipse lamp, bool? on)
        {
            lamp.SetResourceReference(Shape.FillProperty, on == true ? "OkColor" : "PanelBorder");
            lamp.Opacity = on.HasValue ? 1.0 : 0.4;
        }

        // ---------------- Parametre yazma ----------------

        private async void WriteGobekHiz(double value)
        {
            ParameterField field = GobekHizField;
            string oldText = field.Value.HasValue ? field.Value.Value.ToString("F0", Tr) : "—";
            string newText = value.ToString("F0", Tr);
            string description = $"Döner Tabla: Göbek Hızı {oldText} → {newText} %";

            _writingHiz = true;
            field.SetBusy(true);

            try
            {
                await _commands.WriteIntAsync(PlcTags.GobekHiz, (short)value);

                AppLogger.Info($"Parametre yazıldı: {description}");
                ShowStatus($"Kaydedildi: Göbek Hızı = {newText} %", isError: false);
                field.SetValue(value);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Parametre yazılamadı: {description}", ex);
                ShowStatus($"Yazılamadı (Göbek Hızı): {ex.Message}", isError: true);
            }
            finally
            {
                _writingHiz = false;
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
