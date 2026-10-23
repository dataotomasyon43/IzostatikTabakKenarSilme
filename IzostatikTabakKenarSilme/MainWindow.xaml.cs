
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme
{
    public partial class MainWindow : Window
    {
        private bool _isDarkTheme = true;

        // Uygulamadaki tek PLC servisi. Sayfalara constructor ile verilir.
        private readonly PlcService _plcService = new PlcService();

        // Bağlantı kontrolü ve çalışma modu için okunan tag (INT).
        private const string ModeTagName = "Mode";

        // Okuma periyodu: bir okuma + bekleme toplamı en fazla bu süre.
        private static readonly TimeSpan PollInterval =
            TimeSpan.FromMilliseconds(500);

        private readonly CancellationTokenSource _pollCts =
            new CancellationTokenSource();

        private bool _pollingStarted;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_pollingStarted)
                return;

            _pollingStarted = true;

            MainFrame.Navigate(new Pages.AnaEkranPage());

            _ = PollPlcAsync(_pollCts.Token);
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _pollCts.Cancel();

            // Sürmekte olan bir okuma varsa Dispose onun bitmesini bekler
            // (en fazla tag zaman aşımı kadar).
            _plcService.Dispose();
        }

        // Mode tag'ini arka planda periyodik okur. await sonrası kod UI
        // thread'inde devam ettiği için göstergeler doğrudan güncellenir.
        // Bu döngü PLC'ye hiçbir şey YAZMAZ.
        private async Task PollPlcAsync(CancellationToken ct)
        {
            var stopwatch = new Stopwatch();

            while (!ct.IsCancellationRequested)
            {
                stopwatch.Restart();

                try
                {
                    short mode = await Task.Run(
                        () => _plcService.ReadIntTag(ModeTagName));

                    if (ct.IsCancellationRequested)
                        break;

                    ShowPlcConnected(mode);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    ShowPlcDisconnected(ex.Message);
                }

                TimeSpan remaining = PollInterval - stopwatch.Elapsed;

                try
                {
                    if (remaining > TimeSpan.Zero)
                        await Task.Delay(remaining, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private void ShowPlcConnected(short mode)
        {
            PlcStatusText.Text = "● Bağlı";
            PlcStatusText.SetResourceReference(
                TextBlock.ForegroundProperty, "OkColor");
            PlcStatusText.ToolTip = null;

            ModeText.Text = ModeToText(mode);

            StatusBarPlcText.Text = $"PLC: {_plcService.IpAddress} bağlı";
        }

        private void ShowPlcDisconnected(string error)
        {
            PlcStatusText.Text = "● Bağlı değil";
            PlcStatusText.SetResourceReference(
                TextBlock.ForegroundProperty, "ErrorColor");
            PlcStatusText.ToolTip = error;

            // Bağlantı yokken son bilinen modu göstermek yanıltıcı olur.
            ModeText.Text = "-";

            StatusBarPlcText.Text = $"PLC: {_plcService.IpAddress} bağlantı yok";
        }

        // Mode (INT) değer tablosu — PLC programındaki karşılıklar.
        private static string ModeToText(short mode)
        {
            switch (mode)
            {
                case 0: return "Kapalı";
                case 1: return "Güvenli Mod";
                case 2: return "Manuel Mod";
                case 3: return "Otomatik Mod";
                default: return $"Bilinmeyen ({mode})";
            }
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            string themePath = _isDarkTheme
                ? "Themes/LightTheme.xaml"
                : "Themes/DarkTheme.xaml";

            var dictionaries =
                Application.Current.Resources.MergedDictionaries;

            dictionaries.Clear();

            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(themePath, UriKind.Relative)
            });

            _isDarkTheme = !_isDarkTheme;

            ThemeButton.Content = _isDarkTheme
                ? "☀"
                : "  ☾ ";
        }

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;

            if (button == null)
                return;

            switch (button.Tag as string)
            {
                case "Home":
                    MainFrame.Navigate(new Pages.AnaEkranPage());
                    break;

                case "TabakYukle":
                    MainFrame.Navigate(new Pages.TabakYuklePage());
                    break;

                case "CanliIzleme":
                    MainFrame.Navigate(new Pages.CanliIzlemePage(_plcService));
                    break;

                case "Receteler":
                    MainFrame.Navigate(new Pages.RecetelerPage());
                    break;

                case "Ayarlar":
                    MainFrame.Navigate(new Pages.AyarlarPage());
                    break;

                case "AlarmGecmisi":
                    MainFrame.Navigate(new Pages.AlarmGecmisiPage());
                    break;
            }
        }



    }
}
