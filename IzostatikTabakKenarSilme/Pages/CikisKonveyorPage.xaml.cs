using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Shapes;
using IzostatikTabakKenarSilme.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// AYARLAR > Çıkış Konveyör.
    ///
    /// Okuma (yalnız sayfa açıkken): son sensör, hız, yön, istif aktüel /
    /// hedef adet, dönme süresi.
    /// Yazma (yalnız operatör eylemiyle):
    ///   - Konv Hız (0-99 %), İstif Adet (hedef), Dönme Süresi.
    ///   - GERİ (HMI_Cikis_Konveyoru_Yon): kalıcı.
    ///   - KONVEYÖR DÖN: basılı tuttukça TRUE.
    /// Ek koşullar PLC tarafında.
    /// </summary>
    public partial class CikisKonveyorPage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

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
        private readonly HashSet<ParameterField> _writingFields = new HashSet<ParameterField>();

        private bool? _readOk;

        public CikisKonveyorPage(PlcService plc, PlcCommands commands, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent();

            _params = new List<Param>
            {
                new Param { Field = HizField, Tag = PlcTags.KonvHiz, IsInt = true },
                new Param { Field = IstifAdetField, Tag = PlcTags.IstifAdet, IsInt = true },
                new Param { Field = DonmeSureField, Tag = PlcTags.KonvDonmeSure }
            };

            foreach (Param p in _params)
            {
                Param param = p;
                param.Field.ValueEntered += (s, v) => WriteParameter(param, v);
            }

            YonButton.PlcTag = PlcTags.KonvYon;
            YonButton.Commands = _commands;
            YonButton.CommandFailed += (s, m) => ShowStatus(m, isError: true);

            KonvDonButton.PlcTag = PlcTags.KonvManStart;
            KonvDonButton.Commands = _commands;
            KonvDonButton.CommandFailed += (s, m) => ShowStatus(m, isError: true);

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
                SonSensor = _plc.ReadBoolTag(PlcTags.KonvSonSensor),
                Yon = _plc.ReadBoolTag(PlcTags.KonvYon),
                IstifAkt = _plc.ReadIntTag(PlcTags.IstifAktuel)
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            for (int i = 0; i < _params.Count; i++)
                ShowField(_params[i].Field, d.Params[i]);

            SetLamp(SonSensorLamp, d.SonSensor);
            YonButton.SetState(d.Yon);
            IstifAktField.SetValue(d.IstifAkt);

            KonvDonButton.IsEnabled = true;

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
                AppLogger.Warn($"Çıkış Konveyör sayfası: okunamadı: {ex.Message}");
                ShowStatus("Değerler okunamadı: " + ex.Message, isError: true);
            }
        }

        private void ShowUnknown()
        {
            foreach (Param p in _params)
                ShowField(p.Field, null);

            SetLamp(SonSensorLamp, null);
            YonButton.SetState(null);
            IstifAktField.SetValue(null);

            // Basılıysa HoldButton "bırakıldı" verir (FALSE yazılır).
            KonvDonButton.IsEnabled = false;
        }

        private void ShowField(ParameterField field, double? value)
        {
            if (!_writingFields.Contains(field))
                field.SetValue(value);
        }

        private static void SetLamp(Ellipse lamp, bool? on)
        {
            lamp.SetResourceReference(Shape.FillProperty, on == true ? "OkColor" : "PanelBorder");
            lamp.Opacity = on.HasValue ? 1.0 : 0.4;
        }

        // ---------------- Parametre yazma ----------------

        private async void WriteParameter(Param param, double value)
        {
            ParameterField field = param.Field;
            string unit = string.IsNullOrEmpty(field.Unit) ? string.Empty : " " + field.Unit;
            string oldText = field.Value.HasValue ? field.Value.Value.ToString("F" + field.Decimals, Tr) : "—";
            string newText = value.ToString("F" + field.Decimals, Tr);
            string description = $"Çıkış Konveyör: {field.Label} {oldText} → {newText}{unit}";

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
