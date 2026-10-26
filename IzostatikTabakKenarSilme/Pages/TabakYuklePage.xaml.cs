using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IzostatikTabakKenarSilme.Services;
using IzostatikTabakKenarSilme.Services.Tabak;
using Microsoft.Win32;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// ÜRETİM > Tabak Yükle.
    ///
    /// - Form kütüphanesi: EKLE ile seçilen .xlsx dosyaları TabakFormlari
    ///   klasörüne kopyalanır ve listelenir; SİL kaldırır.
    /// - Listeden seçilen form okunur, 360° profil hesaplanır (arka planda),
    ///   kontur düzlemde ve profil açıya göre çizilir.
    /// - PLC bağlıysa modül başlama pozisyonları okunur ve 400-600 mm
    ///   kontrolü gösterilir.
    /// - PLC'YE YÜKLE: onay sonrası TabakUploader (Rec1..6 + el sıkışma).
    /// Yükleme makineyi hareket ettirmez; yalnızca Güvenli/Manuel modda ve
    /// AutoCycle kapalıyken izin verilir.
    /// </summary>
    public partial class TabakYuklePage : Page
    {
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly MachineState _state;
        private readonly TabakLibrary _library = new TabakLibrary();
        private readonly TabakUploader _uploader;

        private TabakLibrary.Entry _selected;
        private TabakProfile _profile;
        private int _pointCount;
        private List<TabakRecipe.ModuleCheck> _modules;
        private bool _busy;
        private bool _checkingModules;

        // Üst üste seçimlerde yalnız sonuncusu ekrana yazılsın.
        private int _loadVersion;

        public TabakYuklePage(PlcService plc, PlcCommands commands, MachineState state)
        {
            if (plc == null) throw new ArgumentNullException(nameof(plc));
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            _uploader = new TabakUploader(plc, commands, state);

            InitializeComponent();

            Loaded += (s, e) =>
            {
                _state.PropertyChanged += State_PropertyChanged;
                RefreshList(null);
                ShowLastLoaded();
                UpdateUploadState();
            };

            // MachineState uygulama boyunca yaşar: abonelik bırakılmazsa
            // sayfa bellekte kalır.
            Unloaded += (s, e) => _state.PropertyChanged -= State_PropertyChanged;
        }

        private void State_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Bağlantı kopunca eski modül değerleri gösterilmesin.
            if (!_state.IsConnected && _modules != null && !_busy)
            {
                _modules = null;
                ShowModules();
            }

            UpdateUploadState();

            // Bağlantı sonradan gelirse modül kontrolünü tamamla.
            if (_state.IsConnected && _modules == null && !_checkingModules && !_busy &&
                _profile != null && _profile.IsValid)
            {
                _ = CheckModulesAsync(_loadVersion);
            }
        }

        // ---------------- Liste ----------------

        private void RefreshList(string selectPath)
        {
            List<TabakLibrary.Entry> items;

            try
            {
                items = _library.List();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Tabak formları listelenemedi", ex);
                ShowStatus("Form klasörü okunamadı: " + ex.Message, isError: true);
                items = new List<TabakLibrary.Entry>();
            }

            FormList.ItemsSource = items;
            FormsTitleText.Text = $"TABAK FORMLARI ({items.Count})";
            EmptyListText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            string path = selectPath ?? _selected?.Path;
            TabakLibrary.Entry match = items.FirstOrDefault(i =>
                string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                FormList.SelectedItem = match;
                FormList.ScrollIntoView(match);
            }
            else
            {
                ClearSelection();
            }

            DeleteButton.IsEnabled = FormList.SelectedItem != null && !_busy;
        }

        private void FormList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteButton.IsEnabled = FormList.SelectedItem != null && !_busy;

            if (FormList.SelectedItem is TabakLibrary.Entry entry)
            {
                if (_selected == null || !string.Equals(_selected.Path, entry.Path, StringComparison.OrdinalIgnoreCase))
                    _ = LoadFormAsync(entry);
            }
        }

        private void FormList_ManipulationBoundaryFeedback(object sender, System.Windows.Input.ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }

        private void ClearSelection()
        {
            _loadVersion++;
            _selected = null;
            _profile = null;
            _modules = null;
            PreviewTitleText.Text = "ÖNİZLEME";
            PlaneHintText.Text = "Listeden bir form seçin";
            PlaneHintText.Visibility = Visibility.Visible;
            ShowStats();
            ShowModules();
            Redraw();
            UpdateUploadState();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Tabak formu ekle",
                Filter = "Excel tabak formu (*.xlsx)|*.xlsx",
                Multiselect = true
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;

            string last = null;

            foreach (string file in dialog.FileNames)
            {
                try
                {
                    string target = _library.TargetPathFor(file);

                    if (string.Equals(System.IO.Path.GetFullPath(file), System.IO.Path.GetFullPath(target),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        last = target;
                        continue; // zaten kütüphanede
                    }

                    if (File.Exists(target) &&
                        MessageBox.Show(Window.GetWindow(this),
                            $"\"{System.IO.Path.GetFileName(target)}\" zaten var. Üzerine yazılsın mı?",
                            "Tabak formu ekle", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    {
                        continue;
                    }

                    last = _library.Import(file);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Tabak formu eklenemedi: {file}", ex);
                    ShowStatus($"Eklenemedi ({System.IO.Path.GetFileName(file)}): {ex.Message}", isError: true);
                }
            }

            // Aynı ad üzerine yazıldıysa yeniden hesaplansın.
            if (last != null && _selected != null &&
                string.Equals(_selected.Path, last, StringComparison.OrdinalIgnoreCase))
            {
                _selected = null;
            }

            RefreshList(last);
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(FormList.SelectedItem is TabakLibrary.Entry entry))
                return;

            if (MessageBox.Show(Window.GetWindow(this),
                    $"\"{entry.Name}\" formu listeden silinsin mi?\n(PLC'deki yüklü form etkilenmez.)",
                    "Tabak formu sil", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _library.Delete(entry);
                _selected = null;
                RefreshList(null);
                ShowStatus($"Silindi: {entry.Name}", isError: false);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Tabak formu silinemedi: {entry.Name}", ex);
                ShowStatus("Silinemedi: " + ex.Message, isError: true);
            }
        }

        // ---------------- Okuma + hesap ----------------

        private async Task LoadFormAsync(TabakLibrary.Entry entry)
        {
            int version = ++_loadVersion;

            _selected = entry;
            _profile = null;
            _modules = null;
            PreviewTitleText.Text = "ÖNİZLEME — " + entry.Name;
            PlaneHintText.Text = "Hesaplanıyor...";
            PlaneHintText.Visibility = Visibility.Visible;
            ShowStats();
            ShowModules();
            Redraw();
            UpdateUploadState();

            try
            {
                var data = await Task.Run(() =>
                {
                    ExcelPointReader.Result read = ExcelPointReader.Read(entry.Path);
                    return new { read, profile = TabakProfile.Compute(read.Points) };
                });

                if (version != _loadVersion)
                    return;

                _profile = data.profile;
                _pointCount = data.read.Points.Count;
                PlaneHintText.Visibility = Visibility.Collapsed;

                ShowStats();
                Redraw();

                if (_profile.IsValid)
                {
                    string skipped = data.read.SkippedRows > 0
                        ? $" ({data.read.SkippedRows} sayısal olmayan satır atlandı)"
                        : string.Empty;
                    ShowStatus($"{entry.Name}: {_pointCount} nokta okundu, 360° profil hesaplandı{skipped}.", isError: false);

                    await CheckModulesAsync(version);
                }
                else
                {
                    ShowStatus($"{entry.Name}: profil geçersiz — {string.Join(" ", _profile.Errors)}", isError: true);
                }
            }
            catch (Exception ex)
            {
                if (version != _loadVersion)
                    return;

                AppLogger.Error($"Tabak formu okunamadı: {entry.Path}", ex);
                PlaneHintText.Text = "Form okunamadı";
                PlaneHintText.Visibility = Visibility.Visible;
                ShowStatus($"{entry.Name} okunamadı: {ex.Message}", isError: true);
            }

            UpdateUploadState();
        }

        private async Task CheckModulesAsync(int version)
        {
            if (_profile == null || !_profile.IsValid || !_state.IsConnected)
            {
                ShowModules();
                UpdateUploadState();
                return;
            }

            _checkingModules = true;

            try
            {
                List<TabakRecipe.ModuleCheck> modules = await _uploader.CheckModulesAsync(_profile);

                if (version != _loadVersion)
                    return;

                _modules = modules;
            }
            catch (Exception ex)
            {
                if (version != _loadVersion)
                    return;

                _modules = null;
                AppLogger.Warn("Modül başlama pozisyonları okunamadı: " + ex.Message);
                ShowStatus("Modül başlama pozisyonları okunamadı: " + ex.Message, isError: true);
            }
            finally
            {
                _checkingModules = false;
            }

            ShowModules();
            UpdateUploadState();
        }

        // ---------------- Yükleme ----------------

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || _profile == null || !_profile.IsValid || _selected == null)
                return;

            string name = _selected.Name;

            if (MessageBox.Show(Window.GetWindow(this),
                    $"\"{name}\" formu PLC'ye yüklenecek ve cam tablosu yeniden oluşturulacak.\n\n" +
                    $"Rmin {_profile.RMin.ToString("0.00", Tr)} mm, Rmax {_profile.RMax.ToString("0.00", Tr)} mm, " +
                    $"kurs {(_profile.RMax - _profile.RMin).ToString("0.00", Tr)} mm.\n\nDevam edilsin mi?",
                    "PLC'ye yükle", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            SetBusy(true);
            var progress = new Progress<string>(m => ShowStatus(m, isError: false));

            TabakUploader.Result result;

            try
            {
                result = await _uploader.UploadAsync(_profile, name, progress);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Tabak yükleme beklenmeyen hata", ex);
                result = new TabakUploader.Result { Success = false, Message = ex.Message };
            }

            if (result.Modules != null && result.Modules.Count > 0)
            {
                _modules = result.Modules;
                ShowModules();
            }

            if (result.Success)
            {
                _library.SaveLastLoaded(name, DateTime.Now);
                ShowLastLoaded();
                ShowStatus("✔ " + result.Message, isError: false);
                SetVerdict("✔ PLC'ye yüklendi", "OkColor");
            }
            else
            {
                ShowStatus("✖ " + result.Message, isError: true);
                SetVerdict("✖ Yükleme başarısız", "ErrorColor");
            }

            SetBusy(false, keepVerdict: true);
        }

        private void SetBusy(bool busy, bool keepVerdict = false)
        {
            _busy = busy;
            FormList.IsEnabled = !busy;
            AddButton.IsEnabled = !busy;
            DeleteButton.IsEnabled = !busy && FormList.SelectedItem != null;

            if (busy)
            {
                UploadButton.IsEnabled = false;
                SetVerdict("Yükleniyor...", "WarningColor");
            }
            else if (!keepVerdict)
            {
                UpdateUploadState();
            }
            else
            {
                UpdateUploadState(keepVerdict: true);
            }
        }

        // Buton yalnız geçerli profil + modül kontrolü tamam + koşullar
        // uygunsa açılır; değilse nedeni gösterilir.
        private void UpdateUploadState(bool keepVerdict = false)
        {
            if (_busy)
                return;

            string reason = null;

            if (_selected == null)
                reason = "Form seçilmedi";
            else if (_profile == null)
                reason = "Hesaplanıyor / okunamadı";
            else if (!_profile.IsValid)
                reason = "Profil geçersiz";
            else
                reason = _uploader.CheckPreconditions();

            if (reason == null)
            {
                if (_modules == null)
                    reason = "Modül pozisyonları okunamadı";
                else if (_modules.Any(m => !m.Ok))
                    reason = "Mekanik sınır aşılıyor";
            }

            UploadButton.IsEnabled = reason == null;

            if (keepVerdict)
                return;

            if (reason == null)
                SetVerdict("✔ Profil geçerli, yüklenebilir", "OkColor");
            else
                SetVerdict(reason, _selected == null ? "SecondaryText" : "WarningColor");
        }

        private void SetVerdict(string text, string colorKey)
        {
            VerdictText.Text = text;
            VerdictText.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
            VerdictBorder.SetResourceReference(Border.BorderBrushProperty, colorKey == "SecondaryText" ? "PanelBorder" : colorKey);
        }

        // ---------------- Bilgi alanları ----------------

        private void ShowLastLoaded()
        {
            Tuple<string, DateTime> last = _library.LoadLastLoaded();
            LastLoadedText.Text = last == null
                ? "—"
                : $"{last.Item1}  ({last.Item2:dd.MM.yyyy HH:mm})";
        }

        private void ShowStats()
        {
            bool ok = _profile != null && _profile.IsValid;

            PointsText.Text = _profile != null ? _pointCount.ToString(Tr) : "—";
            SizeText.Text = _profile != null && _profile.Contour.Count > 0
                ? $"{_profile.Bounds.Width.ToString("0.0", Tr)} × {_profile.Bounds.Height.ToString("0.0", Tr)}"
                : "—";
            RMinText.Text = ok ? $"{_profile.RMin.ToString("0.00", Tr)} ({_profile.RMinAngle}°)" : "—";
            RMaxText.Text = ok ? $"{_profile.RMax.ToString("0.00", Tr)} ({_profile.RMaxAngle}°)" : "—";
            StrokeText.Text = ok ? $"{(_profile.RMax - _profile.RMin).ToString("0.00", Tr)} mm" : "—";
        }

        private void ShowModules()
        {
            ModulePanel.Children.Clear();

            if (_profile == null || !_profile.IsValid)
                return;

            if (_modules == null)
            {
                ModulePanel.Children.Add(new TextBlock
                {
                    Text = _state.IsConnected ? "Okunuyor..." : "PLC bağlı değil",
                    FontSize = 13,
                    Foreground = (Brush)FindResource("SecondaryText")
                });
                return;
            }

            foreach (TabakRecipe.ModuleCheck m in _modules)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock { Text = (m.Ok ? "● " : "✖ ") + m.Name, FontSize = 13 };
                name.SetResourceReference(TextBlock.ForegroundProperty, m.Ok ? "OkColor" : "ErrorColor");

                var range = new TextBlock
                {
                    Text = $"{m.MinPos.ToString("0.0", Tr)} → {m.MaxPos.ToString("0.0", Tr)}",
                    FontSize = 13,
                    FontFamily = new FontFamily("Consolas")
                };
                range.SetResourceReference(TextBlock.ForegroundProperty, m.Ok ? "PrimaryText" : "ErrorColor");
                Grid.SetColumn(range, 1);

                row.Children.Add(name);
                row.Children.Add(range);

                if (!m.Ok)
                    row.ToolTip = m.ViolationText(10);

                ModulePanel.Children.Add(row);
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

        // ---------------- Çizim ----------------

        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Redraw();
        }

        private void Redraw()
        {
            DrawPlane();
            DrawProfile();
        }

        // Düzlem: tabak merkezi ortada, Y yukarı. Kontur, sınırlayıcı
        // dikdörtgen, eksenler, 0° yönü (bomse 0 = profilin başlangıcı),
        // Rmin / Rmax ve hatalı açılar.
        private void DrawPlane()
        {
            Canvas c = PlaneCanvas;
            c.Children.Clear();

            if (_profile == null || _profile.Contour.Count < 2 || c.ActualWidth < 20 || c.ActualHeight < 20)
                return;

            double w = c.ActualWidth, h = c.ActualHeight;
            Point ctr = _profile.Center;
            Rect b = _profile.Bounds;

            double halfX = Math.Max(Math.Abs(b.Left - ctr.X), Math.Abs(b.Right - ctr.X));
            double halfY = Math.Max(Math.Abs(b.Top - ctr.Y), Math.Abs(b.Bottom - ctr.Y));
            double s = Math.Min((w - 70) / (2 * Math.Max(halfX, 1e-6)), (h - 40) / (2 * Math.Max(halfY, 1e-6)));

            Point P(Point q) => new Point(w / 2 + (q.X - ctr.X) * s, h / 2 - (q.Y - ctr.Y) * s);

            // Eksenler
            AddLine(c, new Point(0, h / 2), new Point(w, h / 2), "PanelBorder", 1, dashed: true);
            AddLine(c, new Point(w / 2, 0), new Point(w / 2, h), "PanelBorder", 1, dashed: true);

            // Sınırlayıcı dikdörtgen
            Point tl = P(new Point(b.Left, b.Bottom));
            Point br = P(new Point(b.Right, b.Top));
            var rect = new Rectangle
            {
                Width = Math.Max(0, br.X - tl.X),
                Height = Math.Max(0, br.Y - tl.Y),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 }
            };
            rect.SetResourceReference(Shape.StrokeProperty, "SecondaryText");
            rect.Opacity = 0.5;
            Canvas.SetLeft(rect, tl.X);
            Canvas.SetTop(rect, tl.Y);
            c.Children.Add(rect);

            // Kontur: dolgu + çizgi
            var pts = new PointCollection(_profile.Contour.Select(P));

            var fill = new Polygon { Points = pts, Opacity = 0.15 };
            fill.SetResourceReference(Shape.FillProperty, "AccentColor");
            c.Children.Add(fill);

            var outline = new Polygon { Points = pts, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            outline.SetResourceReference(Shape.StrokeProperty, "AccentColor");
            c.Children.Add(outline);

            // Hatalı açılar
            foreach (int k in _profile.BadAngles)
            {
                double th = k * Math.PI / 180;
                double r = Math.Max(halfX, halfY) * 1.1;
                AddLine(c, P(ctr), P(new Point(ctr.X + r * Math.Cos(th), ctr.Y + r * Math.Sin(th))), "ErrorColor", 1);
            }

            if (_profile.IsValid)
            {
                // 0° yönü
                Point p0 = P(new Point(ctr.X + _profile.Radii[0], ctr.Y));
                AddLine(c, P(ctr), p0, "ModeOtomatikColor", 2);
                AddText(c, "0°", p0.X + 6, p0.Y - 18, "ModeOtomatikColor", 13, bold: true);

                // Rmin / Rmax
                Marker(c, P(ctr), P(PointAt(ctr, _profile.RMaxAngle, _profile.RMax)), "ErrorColor",
                       $"Rmax {_profile.RMax.ToString("0.00", Tr)}");
                Marker(c, P(ctr), P(PointAt(ctr, _profile.RMinAngle, _profile.RMin)), "OkColor",
                       $"Rmin {_profile.RMin.ToString("0.00", Tr)}");
            }

            // Merkez
            Point pc = P(ctr);
            AddLine(c, new Point(pc.X - 7, pc.Y), new Point(pc.X + 7, pc.Y), "PrimaryText", 2);
            AddLine(c, new Point(pc.X, pc.Y - 7), new Point(pc.X, pc.Y + 7), "PrimaryText", 2);

            // Ölçü
            AddText(c, $"{b.Width.ToString("0.0", Tr)} × {b.Height.ToString("0.0", Tr)} mm",
                    6, h - 20, "SecondaryText", 12);
        }

        private static Point PointAt(Point ctr, int deg, double r)
        {
            double th = deg * Math.PI / 180;
            return new Point(ctr.X + r * Math.Cos(th), ctr.Y + r * Math.Sin(th));
        }

        private void Marker(Canvas c, Point from, Point to, string colorKey, string label)
        {
            AddLine(c, from, to, colorKey, 1.5, dashed: true);

            var dot = new Ellipse { Width = 9, Height = 9 };
            dot.SetResourceReference(Shape.FillProperty, colorKey);
            Canvas.SetLeft(dot, to.X - 4.5);
            Canvas.SetTop(dot, to.Y - 4.5);
            c.Children.Add(dot);

            double dx = to.X >= from.X ? 8 : -8 - label.Length * 7;
            AddText(c, label, to.X + dx, to.Y - 8, colorKey, 12, bold: true);
        }

        // Profil: x = açı 0..360°, y = R - Rmin (mm).
        private void DrawProfile()
        {
            Canvas c = ProfileCanvas;
            c.Children.Clear();

            if (_profile == null || !_profile.IsValid || c.ActualWidth < 40 || c.ActualHeight < 30)
                return;

            const double left = 34, bottom = 16, top = 6;
            double w = c.ActualWidth - left - 6, h = c.ActualHeight - bottom - top;
            double stroke = Math.Max(_profile.RMax - _profile.RMin, 1e-6);

            Point P(double deg, double v) => new Point(left + deg / 360.0 * w, top + h - v / stroke * h);

            // Izgara: 90°'lik dikey çizgiler + 0 / max
            for (int deg = 0; deg <= 360; deg += 90)
            {
                AddLine(c, P(deg, 0), P(deg, stroke), "PanelBorder", 1, dashed: true);
                AddText(c, deg + "°", P(deg, 0).X - 10, top + h + 1, "SecondaryText", 10);
            }

            AddLine(c, P(0, 0), P(360, 0), "PanelBorder", 1);
            AddText(c, "0", 4, P(0, 0).Y - 8, "SecondaryText", 10);
            AddText(c, stroke.ToString("0.0", Tr), 0, P(0, stroke).Y - 6, "SecondaryText", 10);

            // Eğri (360° = 0°, kapalı döngü)
            var line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            for (int k = 0; k <= TabakProfile.AngleCount; k++)
            {
                double r = _profile.Radii[k % TabakProfile.AngleCount];
                line.Points.Add(P(k, r - _profile.RMin));
            }

            line.SetResourceReference(Shape.StrokeProperty, "AccentColor");
            c.Children.Add(line);
        }

        private static void AddLine(Canvas c, Point a, Point b, string colorKey, double thickness, bool dashed = false)
        {
            var line = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, StrokeThickness = thickness };
            line.SetResourceReference(Shape.StrokeProperty, colorKey);

            if (dashed)
                line.StrokeDashArray = new DoubleCollection { 4, 3 };

            c.Children.Add(line);
        }

        private static void AddText(Canvas c, string text, double x, double y, string colorKey, double size, bool bold = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
            t.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
            Canvas.SetLeft(t, x);
            Canvas.SetTop(t, y);
            c.Children.Add(t);
        }
    }
}
