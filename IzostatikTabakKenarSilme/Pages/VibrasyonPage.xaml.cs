using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using IzostatikTabakKenarSilme.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// AYARLAR > Vibrasyon Merkezleme.
    ///
    /// Okuma (yalnız sayfa açıkken, PagePoller): üç parametre, manuel
    /// çalıştırma biti ve üç gösterge.
    /// Yazma (yalnız operatör eylemiyle):
    ///   - Parametre kutusunda onaylanan değer → ilgili tag.
    ///   - VİBRASYON DÖN (kalıcı): o anki PLC değerinin tersi →
    ///     HMI_Vibrasyon_Man_Start. Ek koşullar PLC tarafında.
    /// </summary>
    public partial class VibrasyonPage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;

        private bool? _readOk;

        // Yazması süren parametreler (okuma bunların değerini ezmez).
        private readonly HashSet<ParameterField> _writingFields = new HashSet<ParameterField>();

        // Manuel çalıştırma bitinin son okunan değeri (null: bilinmiyor).
        private bool? _manStart;
        private bool _manStartBusy;

        public VibrasyonPage(PlcService plc, PlcCommands commands, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent();

            SureField.ValueEntered += (s, v) =>
                WriteParameter(SureField, () => _commands.WriteRealAsync(PlcTags.VibrasyonSure, (float)v), v);
            BeklemeField.ValueEntered += (s, v) =>
                WriteParameter(BeklemeField, () => _commands.WriteRealAsync(PlcTags.VibrasyonSonrasiBekleme, (float)v), v);
            HizField.ValueEntered += (s, v) =>
                WriteParameter(HizField, () => _commands.WriteIntAsync(PlcTags.VibrasyonHiz, (short)v), v);

            ShowUnknown();

            new PagePoller(this, PollInterval, PollAsync, OnReadError);
        }

        // ---------------- Okuma ----------------

        private async Task PollAsync(CancellationToken ct)
        {
            // Bağlantı yokken deneme; ana pencere bağlantıyı izliyor.
            if (!_state.IsConnected)
            {
                ShowUnknown();
                return;
            }

            var d = await Task.Run(() => new
            {
                Sure = _plc.ReadRealTag(PlcTags.VibrasyonSure),
                Bekleme = _plc.ReadRealTag(PlcTags.VibrasyonSonrasiBekleme),
                Hiz = _plc.ReadIntTag(PlcTags.VibrasyonHiz),
                ManStart = _plc.ReadBoolTag(PlcTags.VibrasyonManStart),
                Calisiyor = _plc.ReadBoolTag(PlcTags.VibrasyonCalisiyor),
                Sensor = _plc.ReadBoolTag(PlcTags.MerkezlemeSensor),
                InvHata = _plc.ReadBoolTag(PlcTags.VibrasyonInvHata)
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            ShowField(SureField, d.Sure);
            ShowField(BeklemeField, d.Bekleme);
            ShowField(HizField, d.Hiz);

            SetManStart(d.ManStart);

            SetLamp(RunLamp, d.Calisiyor, "OkColor");
            SetLamp(SensorLamp, d.Sensor, "OkColor");
            SetLamp(FaultLamp, d.InvHata, "ErrorColor");

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
                AppLogger.Warn($"Vibrasyon sayfası: okunamadı: {ex.Message}");
                ShowStatus("Değerler okunamadı: " + ex.Message, isError: true);
            }
        }

        private void ShowUnknown()
        {
            ShowField(SureField, null);
            ShowField(BeklemeField, null);
            ShowField(HizField, null);
            SetManStart(null);
            SetLamp(RunLamp, null, "OkColor");
            SetLamp(SensorLamp, null, "OkColor");
            SetLamp(FaultLamp, null, "ErrorColor");
        }

        // Yazma sürerken eski okuma yeni değeri ezmesin.
        private void ShowField(ParameterField field, double? value)
        {
            if (!_writingFields.Contains(field))
                field.SetValue(value);
        }

        private static void SetLamp(Ellipse lamp, bool? on, string onColorKey)
        {
            lamp.SetResourceReference(Shape.FillProperty, on == true ? onColorKey : "PanelBorder");
            lamp.Opacity = on.HasValue ? 1.0 : 0.4;
        }

        // ---------------- Parametre yazma ----------------

        private async void WriteParameter(ParameterField field, Func<Task> write, double value)
        {
            string unit = string.IsNullOrEmpty(field.Unit) ? string.Empty : " " + field.Unit;
            string oldText = field.Value.HasValue ? field.Value.Value.ToString("F" + field.Decimals, Tr) : "—";
            string newText = value.ToString("F" + field.Decimals, Tr);
            string description = $"Vibrasyon: {field.Label} {oldText} → {newText}{unit}";

            _writingFields.Add(field);
            field.SetBusy(true);

            try
            {
                await write();

                AppLogger.Info($"Parametre yazıldı: {description}");
                ShowStatus($"Kaydedildi: {field.Label} = {newText}{unit}", isError: false);

                // Bir sonraki okuma PLC'den doğrular.
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

        // ---------------- Manuel çalıştırma (kalıcı) ----------------

        private void SetManStart(bool? on)
        {
            if (_manStartBusy)
                return;

            _manStart = on;
            ApplyManStartVisual(on);
        }

        private void ApplyManStartVisual(bool? on)
        {
            if (on == true)
            {
                ManStartButton.SetResourceReference(ForegroundProperty, "OkColor");
                ManStartButton.Tag = "Active";
                ManStartStateText.Text = "Çalışıyor — durdurmak için tekrar basın.";
            }
            else
            {
                ManStartButton.SetResourceReference(ForegroundProperty, "PrimaryText");
                ManStartButton.Tag = null;
                ManStartStateText.Text = "Bir basış çalıştırır, bir basış durdurur.";
            }

            ManStartButton.IsEnabled = on.HasValue && !_manStartBusy;
        }

        private async void ManStartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_manStart.HasValue || _manStartBusy)
                return;

            bool current = _manStart.Value;
            bool target = !current;
            string description = $"Vibrasyon Dön = {(target ? "AÇIK" : "KAPALI")}";

            _manStartBusy = true;
            ManStartButton.IsEnabled = false;

            try
            {
                await _commands.SetBoolAsync(PlcTags.VibrasyonManStart, target);
                AppLogger.Info($"Manuel komut: {description}");

                _manStartBusy = false;
                _manStart = target;
                ApplyManStartVisual(target);
            }
            catch (Exception ex)
            {
                _manStartBusy = false;
                ApplyManStartVisual(current);

                AppLogger.Error($"Manuel komut gönderilemedi: {description}", ex);
                ShowStatus($"Komut gönderilemedi ({description}): {ex.Message}", isError: true);
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
