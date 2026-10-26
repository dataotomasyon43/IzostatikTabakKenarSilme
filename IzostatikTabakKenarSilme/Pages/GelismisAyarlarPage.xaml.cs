using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using IzostatikTabakKenarSilme.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// GENEL > Gelişmiş Ayarlar: servo tork alarm sınırları.
    ///
    /// PLC (AlarmProg) eksenin anlık torku |Act.Trq| bu sınırı aşınca ilgili
    /// alarmı verir (Reset'e kadar tutar). Bomse/modül: HMI_Ax*_Trq (LREAL),
    /// transferler: HMI_GTRMaxTork / HMI_CTRMaxTork (REAL). % nominal tork,
    /// ekranda 1-1000 girilebilir.
    /// Okuma yalnız sayfa açıkken (1 sn); yazma yalnız operatör değer
    /// onaylayınca.
    /// </summary>
    public partial class GelismisAyarlarPage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private const double MinTork = 1;
        private const double MaxTork = 1000;

        private sealed class Param
        {
            public ParameterField Field;
            public string Tag;
            public bool IsReal; // false: LREAL
        }

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;

        private readonly List<Param> _params = new List<Param>();
        private readonly HashSet<ParameterField> _writingFields = new HashSet<ParameterField>();

        private bool? _readOk;

        public GelismisAyarlarPage(PlcService plc, PlcCommands commands, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent();

            // Eski ekrandaki sıra
            Add(LeftPanel, "Giriş Transfer Servo Tork", PlcTags.TorkGtr, isReal: true);
            Add(LeftPanel, "Çıkış Transfer Servo Tork", PlcTags.TorkCtr, isReal: true);
            Add(LeftPanel, "Bomse-1 Servo Tork", PlcTags.TorkBomse1);
            Add(LeftPanel, "Bomse-2 Servo Tork", PlcTags.TorkBomse2);
            Add(LeftPanel, "Bomse-3 Servo Tork", PlcTags.TorkBomse3);
            Add(LeftPanel, "Bomse-4 Servo Tork", PlcTags.TorkBomse4);
            Add(LeftPanel, "Bomse-5 Servo Tork", PlcTags.TorkBomse5);
            Add(LeftPanel, "Bomse-6 Servo Tork", PlcTags.TorkBomse6);
            Add(LeftPanel, "Bomse-7 Servo Tork", PlcTags.TorkBomse7);
            Add(LeftPanel, "Bomse-8 Servo Tork", PlcTags.TorkBomse8);

            Add(RightPanel, "Çapak Kırma Modül Servo Tork", PlcTags.TorkModCapak);
            Add(RightPanel, "Zımpara-1 Modül Servo Tork", PlcTags.TorkModZim1);
            Add(RightPanel, "Zımpara-2 Modül Servo Tork", PlcTags.TorkModZim2);
            Add(RightPanel, "Sünger-1 Modül Servo Tork", PlcTags.TorkModSun1);
            Add(RightPanel, "Sünger-2 Modül Servo Tork", PlcTags.TorkModSun2);
            Add(RightPanel, "Sünger-3 Modül Servo Tork", PlcTags.TorkModSun3);

            ShowUnknown();

            new PagePoller(this, PollInterval, PollAsync, OnReadError);
        }

        private void Add(Panel panel, string label, string tag, bool isReal = false)
        {
            var field = new ParameterField
            {
                Label = label,
                Unit = "%",
                Decimals = 0,
                Min = MinTork,
                Max = MaxTork,
                ValueWidth = 110,
                UnitWidth = 30,
                ValueFontSize = 22
            };

            var param = new Param { Field = field, Tag = tag, IsReal = isReal };
            field.ValueEntered += (s, v) => WriteParameter(param, v);

            panel.Children.Add(field);
            _params.Add(param);
        }

        // ---------------- Okuma ----------------

        private async Task PollAsync(CancellationToken ct)
        {
            if (!_state.IsConnected)
            {
                ShowUnknown();
                return;
            }

            var reads = _params.Select(p => new { p.Tag, p.IsReal }).ToList();

            List<double> values = await Task.Run(
                () => reads.Select(r => r.IsReal ? _plc.ReadRealTag(r.Tag) : _plc.ReadLRealTag(r.Tag)).ToList(), ct);

            if (ct.IsCancellationRequested)
                return;

            for (int i = 0; i < _params.Count; i++)
                ShowField(_params[i].Field, values[i]);

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
                AppLogger.Warn($"Gelişmiş Ayarlar sayfası: okunamadı: {ex.Message}");
                ShowStatus("Değerler okunamadı: " + ex.Message, isError: true);
            }
        }

        private void ShowUnknown()
        {
            foreach (Param p in _params)
                ShowField(p.Field, null);
        }

        private void ShowField(ParameterField field, double? value)
        {
            if (!_writingFields.Contains(field))
                field.SetValue(value);
        }

        // ---------------- Yazma ----------------

        private async void WriteParameter(Param param, double value)
        {
            ParameterField field = param.Field;
            string oldText = field.Value.HasValue ? field.Value.Value.ToString("F0", Tr) : "—";
            string newText = value.ToString("F0", Tr);
            string description = $"Gelişmiş Ayarlar: {field.Label} {oldText} → {newText} %";

            _writingFields.Add(field);
            field.SetBusy(true);

            try
            {
                if (param.IsReal)
                    await _commands.WriteRealAsync(param.Tag, (float)value);
                else
                    await _commands.WriteLRealAsync(param.Tag, value);

                AppLogger.Info($"Parametre yazıldı: {description}");
                ShowStatus($"Kaydedildi: {field.Label} = {newText} %", isError: false);
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
