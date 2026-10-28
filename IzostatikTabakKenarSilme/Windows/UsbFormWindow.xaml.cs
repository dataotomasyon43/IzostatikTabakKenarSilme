using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IzostatikTabakKenarSilme.Services;
using IzostatikTabakKenarSilme.Services.Tabak;
using Microsoft.Win32;

namespace IzostatikTabakKenarSilme.Windows
{
    /// <summary>
    /// Takılı USB belleklerdeki tabak formu dosyalarını listeler.
    ///
    /// - Yalnız çıkarılabilir sürücüler taranır (alt klasörlerle, derinlik
    ///   ve dosya sayısı sınırlı; sistem klasörleri atlanır).
    /// - .xlsx ve .csv seçilebilir; .xls listede görünür ama "xlsx olarak
    ///   kaydedin" uyarısıyla yüklenemez.
    /// - Pencere açıkken USB takılır / çıkarılırsa liste yenilenir.
    /// - Seçilen dosya okunup tabak profili hesaplanır ve şekli çizilir;
    ///   geçersizse YÜKLE kapalı kalır.
    ///
    /// Ask(...) seçilen dosya yollarını döndürür (İPTAL: null). Kütüphaneye
    /// kopyalama çağıran sayfada yapılır.
    /// </summary>
    public partial class UsbFormWindow : Window
    {
        private const int MaxDepth = 6;
        private const int MaxFiles = 500;

        private static readonly string[] ListedExtensions = { ".xlsx", ".csv", ".xls" };
        private static readonly string[] SkippedFolders = { "System Volume Information", "$RECYCLE.BIN", "RECYCLER" };
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        public sealed class UsbFile
        {
            public string FullPath { get; set; }
            public string FileName { get; set; }
            public string Location { get; set; }
            public bool Supported { get; set; }
            public bool InLibrary { get; set; }

            public string BadgeText => !Supported ? ".XLS — AÇILAMAZ" : InLibrary ? "EKLİ" : null;
            public Brush BadgeBrush { get; set; }
            public Visibility BadgeVisibility => BadgeText != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private readonly TabakLibrary _library;
        private readonly DispatcherTimer _driveTimer;

        private string _driveSignature = string.Empty;
        private bool _checkingDrives;
        private int _scanVersion;
        private int _previewVersion;

        private List<Point> _previewPoints;
        private TabakProfile _previewProfile;

        public List<string> Result { get; private set; }

        private UsbFormWindow(TabakLibrary library)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));

            InitializeComponent();

            // USB takma / çıkarma: hazır çıkarılabilir sürücü listesi değişince yenile.
            _driveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _driveTimer.Tick += async (s, e) =>
            {
                // Sürücü sorgusu (IsReady) bazı okuyucularda yavaş olabilir:
                // arka planda yapılır, üst üste binmez.
                if (_checkingDrives)
                    return;

                _checkingDrives = true;
                try
                {
                    string signature = await Task.Run(() => CurrentDriveSignature());
                    if (signature != _driveSignature)
                        await ScanAsync();
                }
                finally
                {
                    _checkingDrives = false;
                }
            };

            Loaded += async (s, e) =>
            {
                await ScanAsync();
                _driveTimer.Start();
            };

            Closed += (s, e) => _driveTimer.Stop();

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                    Close();
            };
        }

        public static List<string> Ask(Window owner, TabakLibrary library)
        {
            var w = new UsbFormWindow(library);

            if (owner != null && owner.IsVisible)
                w.Owner = owner;
            else
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            w.ShowDialog();
            return w.Result;
        }

        // ---------------- Tarama ----------------

        private static List<DriveInfo> RemovableDrives()
        {
            var list = new List<DriveInfo>();

            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType == DriveType.Removable && d.IsReady)
                        list.Add(d);
                }
                catch
                {
                    // Okunamayan sürücü atlanır.
                }
            }

            return list;
        }

        private static string CurrentDriveSignature()
        {
            return string.Join("|", RemovableDrives().Select(d => d.Name));
        }

        private async Task ScanAsync()
        {
            int version = ++_scanVersion;
            string selected = (FileList.SelectedItem as UsbFile)?.FullPath;

            ScanStatusText.Text = "USB bellekler taranıyor...";

            var libraryNames = new HashSet<string>(
                SafeLibraryList().Select(en => System.IO.Path.GetFileName(en.Path)),
                StringComparer.OrdinalIgnoreCase);

            var scan = await Task.Run(() =>
            {
                List<DriveInfo> drives = RemovableDrives();
                var files = new List<Tuple<DriveInfo, FileInfo>>();

                foreach (DriveInfo d in drives)
                {
                    if (files.Count >= MaxFiles)
                        break;

                    Collect(d, d.RootDirectory, 0, files);
                }

                return Tuple.Create(drives, files);
            });

            if (version != _scanVersion)
                return; // daha yeni bir tarama başladı

            List<DriveInfo> found = scan.Item1;
            _driveSignature = string.Join("|", found.Select(d => d.Name));

            Brush accent = (Brush)FindResource("OkColor");
            Brush warn = (Brush)FindResource("WarningColor");

            List<UsbFile> items = scan.Item2
                .OrderByDescending(t => t.Item2.LastWriteTime)
                .Select(t =>
                {
                    bool supported = ExcelPointReader.IsSupported(t.Item2.Name);
                    return new UsbFile
                    {
                        FullPath = t.Item2.FullName,
                        FileName = t.Item2.Name,
                        Location = $"{DriveLabel(t.Item1)} · {RelativeFolder(t.Item1, t.Item2)} · " +
                                   $"{t.Item2.LastWriteTime.ToString("dd.MM.yyyy HH:mm", Tr)} · {SizeText(t.Item2.Length)}",
                        Supported = supported,
                        InLibrary = supported && libraryNames.Contains(t.Item2.Name),
                        BadgeBrush = supported ? accent : warn
                    };
                })
                .ToList();

            FileList.ItemsSource = items;

            if (found.Count == 0)
            {
                ScanStatusText.Text = "Takılı USB bellek yok";
                EmptyText.Text = "USB bellek takın.\nTakınca liste kendiliğinden yenilenir.\n\nUSB dışındaki bir yerden seçmek için ELLE ARA.";
            }
            else
            {
                ScanStatusText.Text = $"{found.Count} USB bellek · {items.Count} dosya" +
                                      (items.Count >= MaxFiles ? " (ilk " + MaxFiles + ")" : "");
                EmptyText.Text = items.Count == 0
                    ? "USB bellekte .xlsx / .csv tabak formu bulunamadı."
                    : string.Empty;
            }

            EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Önceki seçim hâlâ listedeyse koru.
            UsbFile again = selected == null ? null
                : items.FirstOrDefault(i => string.Equals(i.FullPath, selected, StringComparison.OrdinalIgnoreCase));

            if (again != null)
                FileList.SelectedItem = again;
            else
                ShowPreviewEmpty("Listeden bir dosya seçin");

            AppLogger.Info($"USB form taraması: {found.Count} sürücü, {items.Count} dosya.");
        }

        private List<TabakLibrary.Entry> SafeLibraryList()
        {
            try
            {
                return _library.List();
            }
            catch
            {
                return new List<TabakLibrary.Entry>();
            }
        }

        private static void Collect(DriveInfo drive, DirectoryInfo dir, int depth, List<Tuple<DriveInfo, FileInfo>> files)
        {
            if (files.Count >= MaxFiles)
                return;

            try
            {
                foreach (FileInfo f in dir.EnumerateFiles())
                {
                    if (f.Name.StartsWith("~$") || f.Name.StartsWith("."))
                        continue;

                    if (!ListedExtensions.Any(e => string.Equals(e, f.Extension, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    files.Add(Tuple.Create(drive, f));

                    if (files.Count >= MaxFiles)
                        return;
                }

                if (depth >= MaxDepth)
                    return;

                foreach (DirectoryInfo sub in dir.EnumerateDirectories())
                {
                    if ((sub.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        continue;

                    if (SkippedFolders.Any(n => string.Equals(n, sub.Name, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    Collect(drive, sub, depth + 1, files);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException ||
                                       ex is System.Security.SecurityException)
            {
                // Erişilemeyen klasör atlanır (ör. USB taranırken çıkarıldı).
            }
        }

        private static string DriveLabel(DriveInfo d)
        {
            string label = string.Empty;

            try
            {
                label = d.VolumeLabel;
            }
            catch
            {
            }

            string root = d.Name.TrimEnd('\\');
            return string.IsNullOrWhiteSpace(label) ? root : $"{root} ({label})";
        }

        private static string RelativeFolder(DriveInfo d, FileInfo f)
        {
            string folder = f.DirectoryName ?? string.Empty;
            string rel = folder.Length > d.Name.Length ? folder.Substring(d.Name.Length) : string.Empty;
            return rel.Length == 0 ? "kök klasör" : rel;
        }

        private static string SizeText(long bytes)
        {
            return bytes < 1024 ? $"{bytes} B"
                : bytes < 1024 * 1024 ? $"{(bytes / 1024.0).ToString("0", Tr)} KB"
                : $"{(bytes / 1024.0 / 1024.0).ToString("0.0", Tr)} MB";
        }

        // ---------------- Seçim / önizleme ----------------

        private async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var file = FileList.SelectedItem as UsbFile;
            int version = ++_previewVersion;

            LoadButton.IsEnabled = false;
            _previewPoints = null;
            _previewProfile = null;
            PreviewCanvas.Children.Clear();

            if (file == null)
            {
                ShowPreviewEmpty("Listeden bir dosya seçin");
                return;
            }

            if (!file.Supported)
            {
                ShowPreviewEmpty(".xls açılamıyor");
                ShowInfo(ExcelPointReader.LegacyExcelMessage, isError: true);
                return;
            }

            ShowPreviewEmpty("Okunuyor...");
            PreviewInfoText.Text = string.Empty;

            var check = await Task.Run(() =>
            {
                string error = TabakLibrary.CheckFormat(file.FullPath, out ExcelPointReader.Result r, out TabakProfile p);
                return Tuple.Create(error, r, p);
            });

            if (version != _previewVersion)
                return;

            string formatError = check.Item1;
            ExcelPointReader.Result points = check.Item2;
            TabakProfile profile = check.Item3;

            if (points != null && points.Points.Count >= 2)
            {
                _previewPoints = points.Points;
                _previewProfile = profile;
                PreviewHintText.Visibility = Visibility.Collapsed;
                DrawPreview();
            }
            else
            {
                ShowPreviewEmpty("Format uygun değil");
            }

            if (formatError != null)
            {
                AppLogger.Warn($"USB form uygun değil ({file.FullPath}): {formatError}");
                ShowInfo("✖ Format uygun değil: " + formatError, isError: true);
                return;
            }

            string size = $" · {profile.Bounds.Width.ToString("0.0", Tr)} × {profile.Bounds.Height.ToString("0.0", Tr)} mm";

            ShowInfo($"✔ {points.Points.Count} nokta{size}" +
                     (file.InLibrary ? "\nKütüphanede aynı adlı form var; yüklenirse üzerine yazılması sorulur." : ""),
                     isError: false);
            LoadButton.IsEnabled = true;
        }

        private void ShowPreviewEmpty(string text)
        {
            PreviewCanvas.Children.Clear();
            PreviewHintText.Text = text;
            PreviewHintText.Visibility = Visibility.Visible;
        }

        private void ShowInfo(string text, bool isError)
        {
            PreviewInfoText.Text = text;
            PreviewInfoText.SetResourceReference(TextBlock.ForegroundProperty, isError ? "ErrorColor" : "SecondaryText");
        }

        private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawPreview();
        }

        // Kontur, kutuya oranı korunarak sığdırılır; merkez artı ile
        // gösterilir. Y yukarı (Excel koordinatı) olacak şekilde çevrilir.
        private void DrawPreview()
        {
            PreviewCanvas.Children.Clear();

            if (_previewPoints == null || _previewPoints.Count < 2)
                return;

            double w = PreviewCanvas.ActualWidth, h = PreviewCanvas.ActualHeight;
            if (w < 20 || h < 20)
                return;

            double minX = _previewPoints.Min(p => p.X), maxX = _previewPoints.Max(p => p.X);
            double minY = _previewPoints.Min(p => p.Y), maxY = _previewPoints.Max(p => p.Y);
            double bw = Math.Max(maxX - minX, 1e-6), bh = Math.Max(maxY - minY, 1e-6);

            const double margin = 12;
            double k = Math.Min((w - 2 * margin) / bw, (h - 2 * margin) / bh);
            double ox = (w - bw * k) / 2, oy = (h - bh * k) / 2;

            Point P(Point p) => new Point(ox + (p.X - minX) * k, oy + (maxY - p.Y) * k);

            var poly = new Polygon
            {
                Points = new PointCollection(_previewPoints.Select(P)),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };
            poly.SetResourceReference(Shape.StrokeProperty, "AccentColor");
            poly.Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xF2, 0x8C, 0x28));
            PreviewCanvas.Children.Add(poly);

            if (_previewProfile != null && !_previewProfile.Bounds.IsEmpty)
            {
                Point c = P(_previewProfile.Center);
                AddLine(new Point(c.X - 7, c.Y), new Point(c.X + 7, c.Y));
                AddLine(new Point(c.X, c.Y - 7), new Point(c.X, c.Y + 7));
            }
        }

        private void AddLine(Point a, Point b)
        {
            var line = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, StrokeThickness = 2 };
            line.SetResourceReference(Shape.StrokeProperty, "PrimaryText");
            PreviewCanvas.Children.Add(line);
        }

        // ---------------- Butonlar ----------------

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await ScanAsync();
        }

        private void FileList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (LoadButton.IsEnabled)
                Load_Click(sender, e);
        }

        private void FileList_ManipulationBoundaryFeedback(object sender, System.Windows.Input.ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            if (!(FileList.SelectedItem is UsbFile file) || !file.Supported)
                return;

            Result = new List<string> { file.FullPath };
            Close();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Tabak formu ekle",
                Filter = "Tabak formu (*.xlsx; *.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
                Multiselect = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            Result = dialog.FileNames.ToList();
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            Close();
        }
    }
}
