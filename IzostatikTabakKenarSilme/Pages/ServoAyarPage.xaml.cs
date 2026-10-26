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
    /// ÜRETİM > Servo, iki görünüm:
    ///  1) istasyon grubu başına (Çapak Kırma, Zımparalar, Süngerler) oranlı
    ///     mod, köşeler hızlı, köşe hızı, geri mesafe ve PLC'nin hesapladığı
    ///     geri dönüş pozisyonları;
    ///  2) jog hız/ivme ayarları, eksen referans butonları (basılı tutma)
    ///     ile aktüel pozisyonlar ve bomse bypass (kalıcı) butonları.
    /// Yalnız görünen görünümün değerleri okunur.
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
            public bool IsInt;
        }

        // Referans butonu + üstünde eksenin aktüel pozisyonu
        private sealed class RefAxis
        {
            public string Name, RefTag, PosTag;
            public TextBlock PosText;
        }

        private const string FormHint =
            "Oranlı mod: bomse hızı tabağın şekline göre değişir; köşede Köşe Hızı %, düz kenarda %100. " +
            "Köşeler Hızlı açıkken tersi. Geri dönüş pozisyonu = kurs + başlama poz + geri mesafe (PLC hesaplar).";

        private const string JogHint =
            "Referans: buton basılı tutulduğu sürece TRUE, bırakınca FALSE. Bypass: kalıcı, açıkken yeşil. Ek koşullar PLC tarafında.";

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;

        private readonly List<Group> _groups;
        private readonly List<Param> _params = new List<Param>();
        private readonly HashSet<ParameterField> _writingFields = new HashSet<ParameterField>();

        // Görünüm 2
        private readonly List<Param> _jogParams = new List<Param>();
        private readonly List<RefAxis> _refAxes = new List<RefAxis>();
        private readonly List<PlcToggleButton> _bypassButtons = new List<PlcToggleButton>();

        private bool _jogView;
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

            BuildJogView();
            SelectView(jog: false);

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
                Param p = _params.FirstOrDefault(x => x.Field == f) ?? _jogParams.FirstOrDefault(x => x.Field == f);
                if (p != null)
                    WriteParameter(p, v);
            };

            return f;
        }

        // ---------------- Görünüm 2: jog, referans, bypass ----------------

        private void BuildJogView()
        {
            AddJog("Modül Jog Hızı", PlcTags.ModulJogHiz, true);
            AddJog("GTR CTR Jog Hızı", PlcTags.TransJogHiz, false);
            AddJog("Modül Jog İvme", PlcTags.ModulJogIvme, true);
            AddJog("GTR CTR Jog İvme", PlcTags.TransJogIvme, false);
            AddJog("Bomse Jog Hızı", PlcTags.BomseJogHiz, true);
            AddJog("Bomse Jog İvme", PlcTags.BomseJogIvme, true);

            // Eski HMI'daki sıra
            AddRef(ModulRefPanel, "Çapak Mod", PlcTags.CpkModReferans, PlcTags.CpkModAktPoz);
            AddRef(ModulRefPanel, "Zım1 Mod", PlcTags.Zim1ModReferans, PlcTags.Zim1ModAktPoz);
            AddRef(ModulRefPanel, "Zım2 Mod", PlcTags.Zim2ModReferans, PlcTags.Zim2ModAktPoz);
            AddRef(ModulRefPanel, "Sün1 Mod", PlcTags.Sun1ModReferans, PlcTags.Sun1ModAktPoz);
            AddRef(ModulRefPanel, "Sün2 Mod", PlcTags.Sun2ModReferans, PlcTags.Sun2ModAktPoz);
            AddRef(ModulRefPanel, "GTR", PlcTags.GtrReferans, PlcTags.GtrAktPoz);
            AddRef(ModulRefPanel, "CTR", PlcTags.CtrReferans, PlcTags.CtrAktPoz);
            AddRef(ModulRefPanel, "Sün3 Mod", PlcTags.Sun3ModReferans, PlcTags.Sun3ModAktPoz);

            string[] bomse = { "Giriş", "Çapak", "Zım1", "Zım2", "Sün1", "Sün2", "Sün3", "Çıkış" };

            for (int n = 1; n <= bomse.Length; n++)
            {
                AddRef(BomseRefPanel, "Bomse " + bomse[n - 1], PlcTags.BomseReferans(n), PlcTags.BomseAktPoz(n));
                AddBypass(n, bomse[n - 1]);
            }
        }

        private void AddJog(string label, string tag, bool isInt)
        {
            ParameterField f = Field(label, "%", 0, 0, 100);
            f.Height = 42; // jog + bypass kartları sol sütuna sığsın
            _jogParams.Add(new Param { Field = f, Tag = tag, Group = "Jog", IsInt = isInt });
            JogPanel.Children.Add(f);
        }

        private void AddRef(Panel panel, string name, string refTag, string posTag)
        {
            var pos = new TextBlock
            {
                Text = "—",
                FontSize = 22,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            pos.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");

            var posBox = new Border
            {
                Child = pos,
                Height = 38,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 4)
            };
            posBox.SetResourceReference(Border.BackgroundProperty, "AppBackground");
            posBox.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

            var button = new PlcPushButton
            {
                Content = name.ToUpper(Tr) + " REF",
                PlcTag = refTag,
                Description = name + " Referans",
                Commands = _commands,
                Style = (Style)FindResource("PushStyle")
            };
            button.CommandFailed += (s, m) => ShowStatus(m, isError: true);

            var tile = new StackPanel { Margin = new Thickness(4) };
            tile.Children.Add(posBox);
            tile.Children.Add(button);
            panel.Children.Add(tile);

            _refAxes.Add(new RefAxis { Name = name, RefTag = refTag, PosTag = posTag, PosText = pos });
        }

        private void AddBypass(int n, string name)
        {
            var text = new TextBlock
            {
                Text = $"AX{n}\n{name.ToUpper(Tr)}",
                TextAlignment = TextAlignment.Center,
                LineHeight = 15,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };

            var b = new PlcToggleButton
            {
                Content = text,
                PlcTag = PlcTags.BomseByPass(n),
                Description = $"Bomse Ax{n} ({name}) ByPass",
                Commands = _commands,
                Style = (Style)FindResource("BypassStyle")
            };
            b.CommandFailed += (s, m) => ShowStatus(m, isError: true);

            _bypassButtons.Add(b);
            BypassPanel.Children.Add(b);
        }

        private void FormTab_Click(object sender, RoutedEventArgs e)
        {
            SelectView(jog: false);
        }

        private void JogTab_Click(object sender, RoutedEventArgs e)
        {
            SelectView(jog: true);
        }

        private void SelectView(bool jog)
        {
            _jogView = jog;

            CardsPanel.Visibility = jog ? Visibility.Collapsed : Visibility.Visible;
            JogView.Visibility = jog ? Visibility.Visible : Visibility.Collapsed;

            FormTabButton.Tag = jog ? null : "Active";
            JogTabButton.Tag = jog ? "Active" : null;

            SubtitleText.Text = jog
                ? "Jog hız / ivme, eksen referansı, bomse bypass"
                : "Forma göre bomse hızı (oranlı mod, köşe hızı) ve modül geri mesafeleri";

            // Görünüm değişince eski görünümün değerleri bayat kalmasın
            ShowUnknown();
            _readOk = null;
            ShowStatus(string.Empty, isError: false);
        }

        // ---------------- Okuma ----------------

        private async Task PollAsync(CancellationToken ct)
        {
            if (!_state.IsConnected)
            {
                ShowUnknown();
                return;
            }

            if (_jogView)
            {
                await PollJogAsync(ct);
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

        private async Task PollJogAsync(CancellationToken ct)
        {
            var jogReads = _jogParams.Select(p => new { p.Tag, p.IsInt }).ToList();
            var posTags = _refAxes.Select(a => a.PosTag).ToList();
            var bypassTags = _bypassButtons.Select(b => b.PlcTag).ToList();

            var data = await Task.Run(() => new
            {
                Jog = jogReads.Select(r => r.IsInt ? (double)_plc.ReadIntTag(r.Tag) : _plc.ReadRealTag(r.Tag)).ToList(),
                Pos = posTags.Select(t => _plc.ReadRealTag(t)).ToList(),
                Bypass = bypassTags.Select(t => _plc.ReadBoolTag(t)).ToList()
            }, ct);

            if (ct.IsCancellationRequested || !_jogView)
                return;

            for (int i = 0; i < _jogParams.Count; i++)
                ShowField(_jogParams[i].Field, data.Jog[i]);

            for (int i = 0; i < _refAxes.Count; i++)
                _refAxes[i].PosText.Text = data.Pos[i].ToString("F2", Tr);

            for (int i = 0; i < _bypassButtons.Count; i++)
                _bypassButtons[i].SetState(data.Bypass[i]);

            if (_readOk == false)
                ShowStatus(string.Empty, isError: false);

            _readOk = true;
        }

        private void ShowUnknown()
        {
            foreach (Param p in _jogParams)
                ShowField(p.Field, null);

            foreach (RefAxis a in _refAxes)
                a.PosText.Text = "—";

            foreach (PlcToggleButton b in _bypassButtons)
                b.SetState(null);

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
                if (param.IsInt)
                    await _commands.WriteIntAsync(param.Tag, (short)Math.Round(value));
                else
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
                ? (_jogView ? JogHint : FormHint)
                : $"{DateTime.Now:HH:mm:ss}  {message}";

            PageStatusText.SetResourceReference(
                TextBlock.ForegroundProperty,
                isError ? "ErrorColor" : "SecondaryText");
        }
    }
}
