using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// GENEL > Servis: bakım ve arıza arama ekranı.
    ///  - Girişler / çıkışlar: PLC'nin I_ / Q_ değişkenleri gruplar halinde
    ///    lamba olarak (TRUE yeşil, FALSE sönük, okunamayan kırmızı çerçeve).
    ///    Yalnız durum gösterilir; NC/NO yorumu yapılmaz.
    ///  - Eksenler: 16 servonun aktüel pozisyonu.
    ///  - Sistem: PLC adresi / bağlantı, sürüm, klasörler; logları USB'ye
    ///    kopyalama; tag okuma testi (eski Canlı İzleme).
    /// Yalnız okur, PLC'ye hiçbir şey yazmaz. Yalnız açık görünüm okunur.
    /// </summary>
    public partial class ServisPage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        // Üst üste bu kadar okunamayan tag bu sayfa açıkken bir daha denenmez
        // (yayınlanmamış değişken her saniye zaman aşımı beklemesin).
        private const int MaxFailures = 3;

        private enum View { Inputs, Outputs, Axes, System }

        private sealed class Lamp
        {
            public string Tag;
            public Ellipse Dot;
            public TextBlock Text;
            public int Failures;
        }

        private sealed class AxisRow
        {
            public string Tag;
            public TextBlock Value;
            public int Failures;
        }

        private readonly PlcService _plc;
        private readonly MachineState _state;

        private readonly List<Lamp> _inputs = new List<Lamp>();
        private readonly List<Lamp> _outputs = new List<Lamp>();
        private readonly List<AxisRow> _axes = new List<AxisRow>();
        private readonly Dictionary<string, TextBlock> _info = new Dictionary<string, TextBlock>();

        private readonly List<FrameworkElement> _inputCards = new List<FrameworkElement>();
        private readonly List<FrameworkElement> _outputCards = new List<FrameworkElement>();

        private View _view = View.Inputs;
        private double _lastReadMs;
        private bool _copying;

        public ServisPage(PlcService plc, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent();

            BuildInputs();
            BuildOutputs();
            BuildAxes();
            BuildSystemInfo();

            SelectView(View.Inputs);

            new PagePoller(this, PollInterval, PollAsync, OnReadError);
        }

        // ---------------- Girişler / çıkışlar ----------------

        private void BuildInputs()
        {
            Group(_inputCards, _inputs, 0, "GÜVENLİK",
                ("Acil stop saha 1", "I_Acil_Saha1"),
                ("Acil stop saha 2", "I_Acil_Saha2"),
                ("Acil stop saha 3", "I_Acil_Saha3"),
                ("Pano acil stop basılı", "I_Pano_Acil_Basili"),
                ("Faz kontrol OK", "I_Faz_OK"),
                ("Güvenlik rölesi OK", "I_GuvRole_OK"),
                ("Mico OK", "I_Mico_OK"),
                ("EDM OK", "I_Edm_Ok"));
            Group(_inputCards, _inputs, 0, "İNVERTER HATALARI",
                ("Döner tabla", "I_DonerTabla_Inv_Hata"),
                ("Zımpara-1", "I_Zim1_Inv_Hata"),
                ("Zımpara-2", "I_Zim2_Inv_Hata"),
                ("Sünger-1", "I_Sun1_Inv_Hata"),
                ("Sünger-2", "I_Sun2_Inv_Hata"),
                ("Sünger-3", "I_Sun3_Inv_Hata"),
                ("Vibrasyon", "I_Vib_Inv_Hata"),
                ("Çıkış konveyör", "I_CikisKonv_Inv_Hata"));

            Group(_inputCards, _inputs, 1, "BOMSE SERVO SÜRÜCÜ",
                ("Servo hata", "I_BomServo_Hata"),
                ("HVDC hazır", "I_BomServo_HVDC_Ready"),
                ("VAC hat", "I_BomServo_VAC_Line"));
            Group(_inputCards, _inputs, 1, "BOMSE SENSÖRLERİ",
                Enumerable.Range(1, 8).Select(n => ($"Bomse-{n} sensör", $"I_BomSens{n}")).ToArray());
            Group(_inputCards, _inputs, 1, "ÇIKIŞ KONVEYÖR",
                ("Bant ilk sensör", "I_Cikis_Bandi_Ilk_Sens"),
                ("Bant son sensör", "I_CikisBandi_SonSens"),
                ("Konveyörde tabak var", "I_Cikis_Konv_Tabak_Var"));

            Group(_inputCards, _inputs, 2, "GİRİŞ TRANSFER (GTR)",
                ("Piston 1 yukarıda", "I_GTR_Pist1_Yukarida"),
                ("Piston 1 baskı", "I_GTR_Pist1_Baski"),
                ("Piston 2 yukarıda", "I_GTR_Pist2_Yukarida"),
                ("Piston 2 baskı", "I_GTR_Pist2_Baski"),
                ("Tabak al (pres)", "I_Tabak_Al"),
                ("Merkezleme sensörü", "I_Merkezleme_Sens"));
            Group(_inputCards, _inputs, 2, "ÇIKIŞ TRANSFER (CTR)",
                ("Piston yukarıda", "I_CTR_Piston_Yukarida"),
                ("Piston baskı", "I_CTR_Piston_Baski"),
                ("Piston 2 yukarıda", "I_CTR_Piston2_Yukarida"),
                ("Piston 2 baskı", "I_CTR_Piston2_Baski"));

            Group(_inputCards, _inputs, 3, "DÖNER TABLA",
                ("Tabla yerinde", "I_Tabla_Yerinde"),
                ("Tabla yerinde 2", "I_Tabla_Yerinde_2"),
                ("Baskı aşağıda", "I_Baski_Asagida"),
                ("Baskı yukarıda", "I_Baski_Yukarida"));
            Group(_inputCards, _inputs, 3, "İSTASYONLAR",
                ("Çapak kırma geride", "I_CapakKirmaGeride"),
                ("Çapak kırma ileride", "I_CapakKirmaIleride"),
                ("Sünger-1 geride", "I_Sun1_Geride_Sens"),
                ("Sünger-2 geride", "I_Sun2_Geride_Sens"),
                ("Sünger-3 geride", "I_Sun3_Geride_Sens"));
        }

        private void BuildOutputs()
        {
            Group(_outputCards, _outputs, 0, "GİRİŞ TRANSFER (GTR)",
                ("Piston 1 aşağı", "Q_GTR_P1_Asagi"),
                ("Piston 1 vakum", "Q_GTR_P1_Vak"),
                ("Piston 1 üfleme", "Q_GTR_P1_Uf"),
                ("Piston 2 aşağı", "Q_GTR_P2_Asagi"),
                ("Piston 2 vakum", "Q_GTR_P2_Vak"),
                ("Piston 2 üfleme", "Q_GTR_P2_Uf"),
                ("Üfleme 1", "Q_GTR_Uf1"),
                ("Üfleme 2", "Q_GTR_Uf2"),
                ("Tabak alındı (pres)", "Q_Tabak_Alindi"));
            Group(_outputCards, _outputs, 0, "ÇIKIŞ TRANSFER (CTR)",
                ("Piston aşağı", "Q_CTR_P_Asagi"),
                ("Piston vakum", "Q_CTR_P_Vak"),
                ("Piston üfleme", "Q_CTR_P_Uf"));

            Group(_outputCards, _outputs, 1, "DÖNER TABLA",
                ("Tabla döndürme çalış", "Q_Tabla_Dondurme_Calis"),
                ("Döner tabla run", "Q_Doner_Tabla_Run"),
                ("Döner tabla ileri", "Q_Doner_Tabla_Ileri_Calis"),
                ("Döner tabla geri", "Q_Doner_Tabla_Geri_Calis"),
                ("Tabla baskı aşağı", "Q_Tabla_Baski_Asagi"),
                ("Tabla baskı yukarı", "Q_Tabla_Baski_Yukari"),
                ("Baskı-1 aşağı", "Q_Tabla_Baski1_Asagi"),
                ("Baskı-2 aşağı", "Q_Tabla_Baski2_Asagi"),
                ("Baskı-3 aşağı", "Q_Tabla_Baski3_Asagi"));

            Group(_outputCards, _outputs, 2, "MOTORLAR",
                ("Zımpara-1 ileri", "Q_Zimpara1_Ileri_Calis"),
                ("Zımpara-1 geri", "Q_Zimpara1_Geri_Calis"),
                ("Zımpara-2 ileri", "Q_Zimpara2_Ileri_Calis"),
                ("Zımpara-2 geri", "Q_Zimpara2_Geri_Calis"),
                ("Sünger-1 ileri", "Q_Sunger1_Ileri_Calis"),
                ("Sünger-1 geri", "Q_Sunger1_Geri_Calis"),
                ("Sünger-2 ileri", "Q_Sunger2_Ileri_Calis"),
                ("Sünger-2 geri", "Q_Sunger2_Geri_Calis"),
                ("Sünger-3 ileri", "Q_Sunger3_Ileri_Calis"),
                ("Sünger-3 geri", "Q_Sunger3_Geri_Calis"),
                ("Vibrasyon", "Q_Vibrasyon_Calis"),
                ("Çıkış konveyör ileri", "Q_Cikis_Konveyoru_Ileri_Calis"),
                ("Çıkış konveyör geri", "Q_Cikis_Konveyoru_Geri_Calis"));

            Group(_outputCards, _outputs, 3, "ÜFLEME / HAVA / SU",
                ("Bomse-1 üfleme", "Q_Bom1_Uf"),
                ("Bomse-3 üfleme", "Q_Bom3_Uf"),
                ("Bomse-4 üfleme", "Q_Bom4_Uf"),
                ("Bomse-8 üfleme", "Q_Bom8_Uf"),
                ("Zımpara-1 üfleme", "Q_Zim1_Ufleme"),
                ("Zımpara-2 üfleme", "Q_Zim2_Ufleme"),
                ("Su valfi", "Q_Su_V"),
                ("Rotus hava aç", "Q_Rotus_Hava_Ac"));
            Group(_outputCards, _outputs, 3, "SÜRÜCÜ / RESET",
                ("Bomse servo kontaktör", "Q_BomServo_Kontaktor"),
                ("Bomse servo reset", "Q_BomServo_Reset"),
                ("Güvenlik rölesi reset", "Q_Guv_Role_Reset"),
                ("İnverter arıza reset", "Q_Inv_Ariza_Reset"));
        }

        private void Group(List<FrameworkElement> cards, List<Lamp> lamps, int column, string title,
            params (string Label, string Tag)[] items)
        {
            var stack = new StackPanel();

            var head = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
            head.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            stack.Children.Add(head);

            foreach (var item in items)
            {
                var dot = new Ellipse { Width = 14, Height = 14, Margin = new Thickness(0, 0, 10, 0), StrokeThickness = 2, VerticalAlignment = VerticalAlignment.Center };
                var text = new TextBlock { Text = item.Label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), ToolTip = item.Tag, Background = Brushes.Transparent };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(text);
                stack.Children.Add(row);

                var lamp = new Lamp { Tag = item.Tag, Dot = dot, Text = text };
                SetLamp(lamp, null, unknown: true);
                lamps.Add(lamp);
            }

            var card = new Border { Child = stack };
            card.Style = (Style)FindResource("CardStyle");
            cards.Add(card);

            Column(column).Children.Add(card);
        }

        private StackPanel Column(int i)
        {
            switch (i)
            {
                case 0: return IoCol0;
                case 1: return IoCol1;
                case 2: return IoCol2;
                default: return IoCol3;
            }
        }

        // state: TRUE yeşil, FALSE sönük; okunamadıysa kırmızı çerçeve.
        // unknown: henüz okunmadı / bağlantı yok.
        private static void SetLamp(Lamp lamp, bool? state, bool unknown = false)
        {
            if (state == true)
            {
                lamp.Dot.SetResourceReference(Shape.FillProperty, "OkColor");
                lamp.Dot.Stroke = null;
                lamp.Text.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            }
            else if (state == false || unknown)
            {
                lamp.Dot.SetResourceReference(Shape.FillProperty, "PanelBorder");
                lamp.Dot.Stroke = null;
                lamp.Text.SetResourceReference(TextBlock.ForegroundProperty, unknown ? "SecondaryText" : "PrimaryText");
            }
            else
            {
                lamp.Dot.Fill = Brushes.Transparent;
                lamp.Dot.SetResourceReference(Shape.StrokeProperty, "ErrorColor");
                lamp.Text.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            }
        }

        // ---------------- Eksenler ----------------

        private void BuildAxes()
        {
            string[] bomse = { "Giriş", "Çapak kırma", "Zımpara-1", "Zımpara-2", "Sünger-1", "Sünger-2", "Sünger-3", "Çıkış" };

            for (int n = 1; n <= bomse.Length; n++)
                AxisLine(AxesLeft, $"Bomse-{n} · {bomse[n - 1]}", PlcTags.BomseAktPoz(n), "°");

            AxisLine(AxesRight, "Çapak kırma modül", PlcTags.CpkModAktPoz, "mm");
            AxisLine(AxesRight, "Zımpara-1 modül", PlcTags.Zim1ModAktPoz, "mm");
            AxisLine(AxesRight, "Zımpara-2 modül", PlcTags.Zim2ModAktPoz, "mm");
            AxisLine(AxesRight, "Sünger-1 modül", PlcTags.Sun1ModAktPoz, "mm");
            AxisLine(AxesRight, "Sünger-2 modül", PlcTags.Sun2ModAktPoz, "mm");
            AxisLine(AxesRight, "Sünger-3 modül", PlcTags.Sun3ModAktPoz, "mm");
            AxisLine(AxesRight, "Giriş transfer (GTR)", PlcTags.GtrAktPoz, "mm");
            AxisLine(AxesRight, "Çıkış transfer (CTR)", PlcTags.CtrAktPoz, "mm");
        }

        private void AxisLine(StackPanel panel, string label, string tag, string unit)
        {
            var grid = new Grid { Height = 44, ToolTip = tag };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            var name = new TextBlock { Text = label, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

            var value = new TextBlock
            {
                Text = "—",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(value, 1);

            var u = new TextBlock { Text = unit, FontSize = 14, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            u.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            Grid.SetColumn(u, 2);

            grid.Children.Add(name);
            grid.Children.Add(value);
            grid.Children.Add(u);
            panel.Children.Add(grid);

            _axes.Add(new AxisRow { Tag = tag, Value = value });
        }

        // ---------------- Sistem ----------------

        private void BuildSystemInfo()
        {
            InfoLine("plc", "PLC adresi");
            InfoLine("conn", "PLC bağlantısı");
            InfoLine("read", "Son okuma süresi");
            InfoLine("mode", "Çalışma ortamı");
            InfoLine("ver", "Uygulama sürümü");
            InfoLine("build", "Exe tarihi");
            InfoLine("uptime", "Açık kalma süresi");
            InfoLine("app", "Uygulama klasörü");
            InfoLine("logs", "Log klasörü");

            SetInfo("plc", _plc.IpAddress);
            SetInfo("mode", AppEnvironment.IsDevelopment ? "Geliştirme (bin klasörü, pencere)" : "Saha kurulumu (tam ekran, açılışta başlar)");

            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            SetInfo("ver", asm.GetName().Version?.ToString() ?? "—");

            try
            {
                SetInfo("build", File.GetLastWriteTime(asm.Location).ToString("dd.MM.yyyy HH:mm", Tr));
            }
            catch
            {
                SetInfo("build", "—");
            }

            SetInfo("app", AppDomain.CurrentDomain.BaseDirectory);
            SetInfo("logs", AppLogger.LogDirectory);
        }

        private void InfoLine(string key, string label)
        {
            int row = SystemInfoGrid.RowDefinitions.Count;
            SystemInfoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var name = new TextBlock { Text = label, FontSize = 14, Margin = new Thickness(0, 6, 0, 6) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            Grid.SetRow(name, row);

            var value = new TextBlock { Text = "—", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);

            SystemInfoGrid.Children.Add(name);
            SystemInfoGrid.Children.Add(value);
            _info[key] = value;
        }

        private void SetInfo(string key, string text, string colorKey = "PrimaryText")
        {
            if (!_info.TryGetValue(key, out TextBlock t))
                return;

            t.Text = text;
            t.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
        }

        private void UpdateSystemInfo()
        {
            SetInfo("conn", _state.IsConnected ? "Bağlı" : "Bağlı değil", _state.IsConnected ? "OkColor" : "ErrorColor");
            SetInfo("read", _lastReadMs > 0 ? $"{_lastReadMs.ToString("0", Tr)} ms (açık görünümdeki tag'ler)" : "—");

            TimeSpan up = DateTime.Now - Process.GetCurrentProcess().StartTime;
            SetInfo("uptime", up.TotalDays >= 1
                ? $"{(int)up.TotalDays} gün {up.Hours} sa {up.Minutes} dk"
                : $"{up.Hours} sa {up.Minutes} dk");
        }

        // ---------------- Görünüm ----------------

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender == InputsTab) SelectView(View.Inputs);
            else if (sender == OutputsTab) SelectView(View.Outputs);
            else if (sender == AxesTab) SelectView(View.Axes);
            else SelectView(View.System);
        }

        private void SelectView(View view)
        {
            _view = view;

            InputsTab.Tag = view == View.Inputs ? "Active" : null;
            OutputsTab.Tag = view == View.Outputs ? "Active" : null;
            AxesTab.Tag = view == View.Axes ? "Active" : null;
            SystemTab.Tag = view == View.System ? "Active" : null;

            bool io = view == View.Inputs || view == View.Outputs;
            IoView.Visibility = io ? Visibility.Visible : Visibility.Collapsed;
            AxesView.Visibility = view == View.Axes ? Visibility.Visible : Visibility.Collapsed;
            SystemView.Visibility = view == View.System ? Visibility.Visible : Visibility.Collapsed;

            foreach (FrameworkElement c in _inputCards)
                c.Visibility = view == View.Inputs ? Visibility.Visible : Visibility.Collapsed;
            foreach (FrameworkElement c in _outputCards)
                c.Visibility = view == View.Outputs ? Visibility.Visible : Visibility.Collapsed;

            if (io)
                IoView.ScrollToTop();

            switch (view)
            {
                case View.Inputs:
                    SubtitleText.Text = "PLC girişleri (yeşil: TRUE)";
                    ShowStatus("Lamba yalnız sinyalin durumunu gösterir (yeşil = TRUE). Kırmızı çerçeve: değişken okunamadı. Satıra uzun basınca tag adı görünür.");
                    break;
                case View.Outputs:
                    SubtitleText.Text = "PLC çıkışları (yeşil: TRUE)";
                    ShowStatus("Yalnız gösterge; buradan çıkış sürülmez.");
                    break;
                case View.Axes:
                    SubtitleText.Text = "Servo eksenlerinin aktüel pozisyonu";
                    ShowStatus(string.Empty);
                    break;
                default:
                    SubtitleText.Text = "Bağlantı, sürüm, loglar";
                    ShowStatus(string.Empty);
                    UpdateSystemInfo();
                    break;
            }
        }

        // ---------------- Okuma ----------------

        private async Task PollAsync(CancellationToken ct)
        {
            View view = _view;

            if (view == View.System)
            {
                UpdateSystemInfo();
                return;
            }

            if (!_state.IsConnected)
            {
                ShowUnknown(view);
                return;
            }

            var sw = Stopwatch.StartNew();

            if (view == View.Axes)
            {
                var rows = _axes.Where(a => a.Failures < MaxFailures).ToList();
                var tags = rows.Select(a => a.Tag).ToList();

                double?[] values = await Task.Run(() => tags.Select(t => Try(() => (double)_plc.ReadRealTag(t))).ToArray(), ct);

                if (ct.IsCancellationRequested || _view != view)
                    return;

                for (int i = 0; i < rows.Count; i++)
                {
                    AxisRow r = rows[i];

                    if (values[i].HasValue)
                    {
                        r.Failures = 0;
                        r.Value.Text = values[i].Value.ToString("F2", Tr);
                        r.Value.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                    }
                    else
                    {
                        r.Failures++;
                        r.Value.Text = "okunamadı";
                        r.Value.SetResourceReference(TextBlock.ForegroundProperty, "ErrorColor");
                    }
                }
            }
            else
            {
                List<Lamp> lamps = (view == View.Inputs ? _inputs : _outputs).Where(l => l.Failures < MaxFailures).ToList();
                var tags = lamps.Select(l => l.Tag).ToList();

                bool?[] values = await Task.Run(() => tags.Select(t => TryBool(t)).ToArray(), ct);

                if (ct.IsCancellationRequested || _view != view)
                    return;

                for (int i = 0; i < lamps.Count; i++)
                {
                    if (values[i].HasValue)
                        lamps[i].Failures = 0;
                    else if (++lamps[i].Failures == MaxFailures)
                        AppLogger.Warn($"Servis: {lamps[i].Tag} okunamıyor, bu sayfa açıkken bir daha denenmeyecek.");

                    SetLamp(lamps[i], values[i]);
                }
            }

            _lastReadMs = sw.Elapsed.TotalMilliseconds;
        }

        private bool? TryBool(string tag)
        {
            try
            {
                return _plc.ReadBoolTag(tag);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static double? Try(Func<double> read)
        {
            try
            {
                return read();
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private void ShowUnknown(View view)
        {
            if (view == View.Axes)
            {
                foreach (AxisRow r in _axes)
                {
                    r.Value.Text = "—";
                    r.Value.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                }
            }
            else
            {
                foreach (Lamp l in view == View.Inputs ? _inputs : _outputs)
                    SetLamp(l, null, unknown: true);
            }
        }

        private void OnReadError(Exception ex)
        {
            ShowUnknown(_view);
            ShowStatus("Okunamadı: " + ex.Message, isError: true);
        }

        // ---------------- Loglar / test ----------------

        private async void CopyLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_copying)
                return;

            _copying = true;
            ShowStatus("Loglar kopyalanıyor...");

            try
            {
                string result = await Task.Run(() => CopyLogsToUsb());
                ShowStatus(result, isError: result.StartsWith("✖"));
            }
            catch (Exception ex)
            {
                AppLogger.Error("Loglar USB'ye kopyalanamadı", ex);
                ShowStatus("✖ Kopyalanamadı: " + ex.Message, isError: true);
            }
            finally
            {
                _copying = false;
            }
        }

        // Log dosyaları (son 14 gün) ve alarm geçmişi ilk takılı USB belleğe:
        // <USB>\IzostatikLoglar\yyyyMMdd_HHmmss\
        private static string CopyLogsToUsb()
        {
            DriveInfo usb = DriveInfo.GetDrives().FirstOrDefault(d =>
            {
                try { return d.DriveType == DriveType.Removable && d.IsReady; }
                catch { return false; }
            });

            if (usb == null)
                return "✖ Takılı USB bellek yok. USB takıp tekrar deneyin.";

            string target = System.IO.Path.Combine(usb.RootDirectory.FullName, "IzostatikLoglar",
                DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(target);

            int count = 0;

            if (Directory.Exists(AppLogger.LogDirectory))
            {
                DateTime since = DateTime.Now.AddDays(-14);

                foreach (FileInfo f in new DirectoryInfo(AppLogger.LogDirectory).GetFiles("*.log").Where(f => f.LastWriteTime >= since))
                {
                    CopyShared(f.FullName, System.IO.Path.Combine(target, f.Name));
                    count++;
                }
            }

            string alarms = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AlarmGecmisi");

            if (Directory.Exists(alarms))
            {
                string alarmTarget = System.IO.Path.Combine(target, "AlarmGecmisi");
                Directory.CreateDirectory(alarmTarget);

                foreach (FileInfo f in new DirectoryInfo(alarms).GetFiles())
                {
                    CopyShared(f.FullName, System.IO.Path.Combine(alarmTarget, f.Name));
                    count++;
                }
            }

            AppLogger.Info($"Loglar USB'ye kopyalandı: {target} ({count} dosya)");
            return $"✔ {count} dosya kopyalandı: {target}";
        }

        // Yazılmakta olan log dosyası da kopyalanabilsin.
        private static void CopyShared(string source, string target)
        {
            using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write))
                src.CopyTo(dst);
        }

        private void TagTest_Click(object sender, RoutedEventArgs e)
        {
            PlcService plc = _plc;
            MachineState state = _state;
            NavigationService?.Navigate(new CanliIzlemePage(plc, () => new ServisPage(plc, state)));
        }

        private void Scroll_ManipulationBoundaryFeedback(object sender, System.Windows.Input.ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }

        private void ShowStatus(string message, bool isError = false)
        {
            PageStatusText.Text = message;
            PageStatusText.SetResourceReference(TextBlock.ForegroundProperty, isError ? "ErrorColor" : "SecondaryText");
        }
    }
}
