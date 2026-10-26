using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using IzostatikTabakKenarSilme.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// ÜRETİM > Servo: istasyon grubu başına (Çapak Kırma, Zımparalar,
    /// Süngerler) oranlı mod, köşeler hızlı, köşe hızı, geri mesafe ve
    /// PLC'nin hesapladığı geri dönüş pozisyonları.
    ///
    /// Okuma yalnız sayfa açıkken (1 sn); yazma yalnız operatör değer
    /// onaylayınca / butona basınca. Ek koşullar PLC tarafında.
    /// </summary>
    public partial class ServoAyarPage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        // Modül pozisyonu üst sınırı (Tabak Yükle ile aynı).
        private const double MaxPosMm = 600.0;

        private sealed class Group
        {
            public string Title;
            public string OranliMod, KoselerHizli, KoseHiz, GeriMesafe;
            public KeyValuePair<string, string>[] GeriPoz; // ad → tag

            // Oluşturulan kontroller
            public PlcToggleButton OranliButton, KoselerButton;
            public ParameterField KoseHizField, GeriMesafeField;
            public List<ParameterField> GeriPozFields = new List<ParameterField>();
            public TextBlock WarningText;
        }

        private sealed class Param
        {
            public ParameterField Field;
            public string Tag;
            public string Group;
        }

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;

        private readonly List<Group> _groups;
        private readonly List<Param> _params = new List<Param>();
        private readonly HashSet<ParameterField> _writingFields = new HashSet<ParameterField>();

        private bool? _readOk;

        public ServoAyarPage(PlcService plc, PlcCommands commands, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent();

            _groups = new List<Group>
            {
                new Group
                {
                    Title = "ÇAPAK KIRMA",
                    OranliMod = PlcTags.CpkOranliMod, KoselerHizli = PlcTags.CpkKoselerHizli,
                    KoseHiz = PlcTags.CpkKoseHiz, GeriMesafe = PlcTags.CpkGeriMesafe,
                    GeriPoz = new[] { Pair("Çapak Kırma", PlcTags.CpkGeriPoz) }
                },
                new Group
                {
                    Title = "ZIMPARALAR",
                    OranliMod = PlcTags.ZimOranliMod, KoselerHizli = PlcTags.ZimKoselerHizli,
                    KoseHiz = PlcTags.ZimKoseHiz, GeriMesafe = PlcTags.ZimGeriMesafe,
                    GeriPoz = new[] { Pair("Zımpara-1", PlcTags.Zim1GeriPoz), Pair("Zımpara-2", PlcTags.Zim2GeriPoz) }
                },
                new Group
                {
                    Title = "SÜNGERLER",
                    OranliMod = PlcTags.SunOranliMod, KoselerHizli = PlcTags.SunKoselerHizli,
                    KoseHiz = PlcTags.SunKoseHiz, GeriMesafe = PlcTags.SunGeriMesafe,
                    GeriPoz = new[]
                    {
                        Pair("Sünger-1", PlcTags.Sun1GeriPoz), Pair("Sünger-2", PlcTags.Sun2GeriPoz),
                        Pair("Sünger-3", PlcTags.Sun3GeriPoz)
                    }
                }
            };

            foreach (Group g in _groups)
                CardsPanel.Children.Add(BuildCard(g));

            ShowUnknown();

            new PagePoller(this, PollInterval, PollAsync, OnReadError);
        }

        private static KeyValuePair<string, string> Pair(string name, string tag)
        {
            return new KeyValuePair<string, string>(name, tag);
        }

        // ---------------- Kart ----------------

        private Border BuildCard(Group g)
        {
            var stack = new StackPanel();

            var title = new TextBlock { Text = g.Title, FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
            title.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            stack.Children.Add(title);

            // Kalıcı butonlar
            var toggles = new UniformGrid { Columns = 2, Margin = new Thickness(-3, 0, -3, 4) };
            g.OranliButton = Toggle("ORANLI MOD", g.OranliMod, $"{g.Title} Oranlı Mod");
            g.KoselerButton = Toggle("KÖŞELER HIZLI", g.KoselerHizli, $"{g.Title} Köşeler Hızlı");
            toggles.Children.Add(g.OranliButton);
            toggles.Children.Add(g.KoselerButton);
            stack.Children.Add(toggles);

            // Parametreler
            g.KoseHizField = Field("Köşe Hızı", "%", 0, 1, 100);
            g.GeriMesafeField = Field("Geri Mesafe", "mm", 1, 0, 100);
            stack.Children.Add(g.KoseHizField);
            stack.Children.Add(g.GeriMesafeField);

            _params.Add(new Param { Field = g.KoseHizField, Tag = g.KoseHiz, Group = g.Title });
            _params.Add(new Param { Field = g.GeriMesafeField, Tag = g.GeriMesafe, Group = g.Title });

            // Geri dönüş pozisyonları (PLC hesaplar)
            var sep = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 6) };
            sep.SetResourceReference(Border.BackgroundProperty, "PanelBorder");
            stack.Children.Add(sep);

            var sub = new TextBlock { Text = "GERİ DÖNÜŞ POZİSYONU", FontSize = 11, FontWeight = FontWeights.Bold };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            stack.Children.Add(sub);

            foreach (KeyValuePair<string, string> gp in g.GeriPoz)
            {
                ParameterField f = Field(gp.Key, "mm", 2, 0, 0);
                f.IsReadOnly = true;
                g.GeriPozFields.Add(f);
                stack.Children.Add(f);
            }

            g.WarningText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            g.WarningText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorColor");
            stack.Children.Add(g.WarningText);

            var card = new Border { Child = stack, VerticalAlignment = VerticalAlignment.Top };
            card.Style = (Style)FindResource("CardStyle");
            return card;
        }

        private PlcToggleButton Toggle(string text, string tag, string description)
        {
            var b = new PlcToggleButton
            {
                Content = text,
                PlcTag = tag,
                Description = description,
                Commands = _commands,
                Style = (Style)FindResource("ToggleStyle")
            };

            b.CommandFailed += (s, m) => ShowStatus(m, isError: true);
            return b;
        }

        private ParameterField Field(string label, string unit, int decimals, double min, double max)
        {
            var f = new ParameterField
            {
                Label = label,
                Unit = unit,
                Decimals = decimals,
                Min = min,
                Max = max,
                ValueWidth = 100,
                UnitWidth = 34,
                ValueFontSize = 22
            };

            f.ValueEntered += (s, v) =>
            {
                Param p = _params.FirstOrDefault(x => x.Field == f);
                if (p != null)
                    WriteParameter(p, v);
            };

            return f;
        }

        // ---------------- Okuma ----------------

        private async Task PollAsync(CancellationToken ct)
        {
            if (!_state.IsConnected)
            {
                ShowUnknown();
                return;
            }

            var reads = _groups.Select(g => new
            {
                g.OranliMod,
                g.KoselerHizli,
                g.KoseHiz,
                g.GeriMesafe,
                GeriPoz = g.GeriPoz.Select(p => p.Value).ToList()
            }).ToList();

            var data = await Task.Run(() => reads.Select(r => new
            {
                Oranli = _plc.ReadBoolTag(r.OranliMod),
                Koseler = _plc.ReadBoolTag(r.KoselerHizli),
                KoseHiz = _plc.ReadRealTag(r.KoseHiz),
                GeriMesafe = _plc.ReadRealTag(r.GeriMesafe),
                GeriPoz = r.GeriPoz.Select(t => _plc.ReadRealTag(t)).ToList()
            }).ToList(), ct);

            if (ct.IsCancellationRequested)
                return;

            for (int i = 0; i < _groups.Count; i++)
            {
                Group g = _groups[i];
                var d = data[i];

                g.OranliButton.SetState(d.Oranli);
                g.KoselerButton.SetState(d.Koseler);
                ShowField(g.KoseHizField, d.KoseHiz);
                ShowField(g.GeriMesafeField, d.GeriMesafe);

                var over = new List<string>();
                for (int j = 0; j < g.GeriPozFields.Count; j++)
                {
                    g.GeriPozFields[j].SetValue(d.GeriPoz[j]);
                    if (d.GeriPoz[j] > MaxPosMm)
                        over.Add($"{g.GeriPoz[j].Key} {d.GeriPoz[j].ToString("0.0", Tr)}");
                }

                g.WarningText.Text = over.Count == 0
                    ? string.Empty
                    : $"⚠ {MaxPosMm:0} mm aşılıyor: {string.Join(", ", over)}";
            }

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
                AppLogger.Warn($"Servo sayfası: okunamadı: {ex.Message}");
                ShowStatus("Değerler okunamadı: " + ex.Message, isError: true);
            }
        }

        private void ShowUnknown()
        {
            foreach (Group g in _groups)
            {
                g.OranliButton.SetState(null);
                g.KoselerButton.SetState(null);
                ShowField(g.KoseHizField, null);
                ShowField(g.GeriMesafeField, null);

                foreach (ParameterField f in g.GeriPozFields)
                    f.SetValue(null);

                g.WarningText.Text = string.Empty;
            }
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
            string unit = " " + field.Unit;
            string oldText = field.Value.HasValue ? field.Value.Value.ToString("F" + field.Decimals, Tr) : "—";
            string newText = value.ToString("F" + field.Decimals, Tr);
            string description = $"Servo {param.Group}: {field.Label} {oldText} → {newText}{unit}";

            _writingFields.Add(field);
            field.SetBusy(true);

            try
            {
                await _commands.WriteRealAsync(param.Tag, (float)value);

                AppLogger.Info($"Parametre yazıldı: {description}");
                ShowStatus($"Kaydedildi: {param.Group} {field.Label} = {newText}{unit}", isError: false);
                field.SetValue(value);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Parametre yazılamadı: {description}", ex);
                ShowStatus($"Yazılamadı ({param.Group} {field.Label}): {ex.Message}", isError: true);
            }
            finally
            {
                _writingFields.Remove(field);
                field.SetBusy(false);
            }
        }

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
