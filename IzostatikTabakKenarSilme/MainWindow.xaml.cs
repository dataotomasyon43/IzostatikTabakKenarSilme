
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using IzostatikTabakKenarSilme.Services;
using IzostatikTabakKenarSilme.Windows;

namespace IzostatikTabakKenarSilme
{
    public partial class MainWindow : Window
    {
        private bool _isDarkTheme = true;

        // Uygulamadaki tek PLC servisi. Sayfalara constructor ile verilir.
        private readonly PlcService _plcService = new PlcService();

        // Tüm PLC yazmaları (mod butonları, alarm reset) bu kuyruktan,
        // geliş sırasıyla gider.
        private readonly PlcWriteQueue _writeQueue = new PlcWriteQueue();

        // Alarm izleme, alarm metinleri ve geçmiş kaydı.
        private readonly AlarmService _alarmService = new AlarmService();

        private AktifAlarmlarWindow _alarmWindow;

        // Okuma periyodu: bir tur (Mode + Alm) + bekleme toplamı en fazla bu süre.
        private static readonly TimeSpan PollInterval =
            TimeSpan.FromMilliseconds(500);

        // Uygulama kapanırken bekleyen PLC yazmaları (ör. reset FALSE)
        // için en fazla beklenecek süre.
        private static readonly TimeSpan ShutdownWriteTimeout =
            TimeSpan.FromSeconds(4);

        private readonly CancellationTokenSource _pollCts =
            new CancellationTokenSource();

        private bool _pollingStarted;

        // Son bilinen durum; yalnızca değişimlerde loglamak için.
        private bool? _plcConnected;
        private short? _lastMode;
        private bool? _alarmReadOk;
        private bool? _autoCycle;          // null: okunamadı / bilinmiyor
        private bool? _autoCycleReadOk;
        private string _machineState;      // son gösterilen makine durumu (log için)

        // Alt çubuktaki arıza akışı: aktif arızaların açıklamaları
        // saniyede bir sırayla gösterilir.
        private readonly DispatcherTimer _alarmTickerTimer =
            new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private int _alarmTickerIndex;

        // USB ışıklı buton modülü (Pro-face EZ, 5 buton + 5 RGB lamba).
        private readonly EzSwitchDevice _ezSwitch = new EzSwitchDevice();
        private readonly CancellationTokenSource _ezCts = new CancellationTokenSource();
        private Task _ezAnimation;
        private bool _ezStartupPlayed;

        public MainWindow()
        {
            InitializeComponent();

            _alarmTickerTimer.Tick += (s, e) => UpdateAlarmTicker(advance: true);

            // Frame, nesneyle açılan her sayfayı geri gitme geçmişinde
            // tutar. Geri gitme kullanılmadığı ve HMI günlerce açık kaldığı
            // için geçmiş her geçişte temizlenir (bellek birikmesin).
            MainFrame.Navigated += (s, e) =>
            {
                while (MainFrame.CanGoBack)
                    MainFrame.RemoveBackEntry();
            };

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_pollingStarted)
                return;

            _pollingStarted = true;

            _alarmService.Initialize();

            NavigateTo("Otomatik");

            _alarmTickerTimer.Start();

            StartEzSwitch();

            _ = PollPlcAsync(_pollCts.Token);
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // Alarm penceresi önce kapatılır: reset butonu basılıysa
            // FALSE yazması kuyruğa bu sırada eklenir.
            _alarmWindow?.Close();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _alarmTickerTimer.Stop();
            _pollCts.Cancel();

            StopEzSwitch();

            // Kuyrukta bekleyen yazmalar (ör. HMI_Reset = FALSE) PLC servisi
            // kapatılmadan önce gönderilsin.
            _writeQueue.WaitForIdle(ShutdownWriteTimeout);

            // Sürmekte olan bir okuma varsa Dispose onun bitmesini bekler
            // (en fazla tag zaman aşımı kadar).
            _plcService.Dispose();
        }

        // Mode ve Alm tag'lerini arka planda periyodik okur. await sonrası
        // kod UI thread'inde devam ettiği için göstergeler doğrudan
        // güncellenir. Bu döngü PLC'ye hiçbir şey YAZMAZ.
        private async Task PollPlcAsync(CancellationToken ct)
        {
            var stopwatch = new Stopwatch();

            AppLogger.Info($"PLC izleme başladı ({_plcService.IpAddress}, tag: {PlcTags.Mode} + {PlcTags.AlarmArray}[{PlcTags.AlarmCount}], periyot: {PollInterval.TotalMilliseconds} ms).");

            while (!ct.IsCancellationRequested)
            {
                stopwatch.Restart();

                // Bu döngü hiçbir hata yüzünden durmamalı: her tur kendi
                // içinde korunur, beklenmeyen hatalar loglanıp devam edilir.
                try
                {
                    bool connected = false;

                    // 1) Bağlantı kontrolü + çalışma modu
                    try
                    {
                        short mode = await Task.Run(
                            () => _plcService.ReadIntTag(PlcTags.Mode));

                        if (ct.IsCancellationRequested)
                            break;

                        ShowPlcConnected(mode);
                        connected = true;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (ct.IsCancellationRequested)
                            break;

                        ShowPlcDisconnected(ex);
                    }

                    // 2) Çalışıyor biti (yalnızca bağlantı varken)
                    if (connected)
                    {
                        try
                        {
                            bool running = await Task.Run(
                                () => _plcService.ReadBoolTag(PlcTags.AutoCycle));

                            if (ct.IsCancellationRequested)
                                break;

                            _autoCycle = running;

                            if (_autoCycleReadOk == false)
                                AppLogger.Info($"{PlcTags.AutoCycle} yeniden okunabiliyor.");

                            _autoCycleReadOk = true;
                        }
                        catch (ObjectDisposedException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (ct.IsCancellationRequested)
                                break;

                            _autoCycle = null;

                            if (_autoCycleReadOk != false)
                            {
                                _autoCycleReadOk = false;
                                AppLogger.Warn($"{PlcTags.AutoCycle} okunamadı: {FriendlyPlcError(ex)} [{ex.GetType().Name}: {ex.Message}]");
                            }
                        }
                    }

                    // 3) Alarm dizisi (yalnızca bağlantı varken)
                    if (connected)
                    {
                        try
                        {
                            bool[] alarms = await Task.Run(
                                () => _plcService.ReadAlarmArray(PlcTags.AlarmArray, PlcTags.AlarmCount));

                            if (ct.IsCancellationRequested)
                                break;

                            HandleAlarms(alarms);
                        }
                        catch (ObjectDisposedException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (ct.IsCancellationRequested)
                                break;

                            ShowAlarmReadError(ex);
                        }
                    }

                    // 4) Makine durumu kartı (bağlantı, arıza, çalışma, mod)
                    UpdateMachineState();

                    // 5) Alt çubuk arıza akışı: yeni gelen/giden arıza bir
                    //    sonraki saniyeyi beklemeden yansısın.
                    UpdateAlarmTicker(advance: false);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("PLC izleme döngüsünde beklenmeyen hata", ex);
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

        // ---------------- Alarmlar ----------------

        private void HandleAlarms(bool[] alarms)
        {
            if (_alarmReadOk == false)
            {
                AppLogger.Info("Alarm dizisi yeniden okunabiliyor.");
                ShowSystemMessage("Alarm okuması düzeldi.", isError: false);
            }

            _alarmReadOk = true;

            int raised = _alarmService.Update(alarms);

            // Yeni alarm geldiyse pencere açılır (kapatılmışsa yeniden).
            if (raised > 0)
            {
                ShowSystemMessage($"{raised} yeni alarm.", isError: true);
                ShowActiveAlarms();
            }
        }

        private void ShowAlarmReadError(Exception ex)
        {
            if (_alarmReadOk != false)
            {
                _alarmReadOk = false;
                string error = FriendlyPlcError(ex);
                AppLogger.Warn($"{PlcTags.AlarmArray} okunamadı: {error} [{ex.GetType().Name}: {ex.Message}]");
                ShowSystemMessage("Alarm okunamadı: " + error, isError: true);
            }
        }

        // Aktif alarmlar penceresini açar; açıksa öne getirir.
        // Alarm Geçmişi sayfasından da çağrılır.
        public void ShowActiveAlarms()
        {
            if (_alarmWindow == null)
            {
                _alarmWindow = new AktifAlarmlarWindow(_plcService, _writeQueue, _alarmService)
                {
                    Owner = this
                };

                _alarmWindow.Closed += (s, e) => _alarmWindow = null;
                _alarmWindow.Show();
            }
            else
            {
                if (_alarmWindow.WindowState == WindowState.Minimized)
                    _alarmWindow.WindowState = WindowState.Normal;

                _alarmWindow.Activate();
            }
        }

        // ---------------- Çalışma modu ----------------

        // Mod butonları: Mode tag'ine butonun CommandParameter değerini yazar.
        // Yalnızca operatör tıkladığında çalışır.
        private async void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;

            if (button == null || !short.TryParse(button.CommandParameter as string, out short value))
                return;

            string text = MachineModes.ToText(value);

            ModeButtonsPanel.IsEnabled = false;
            AppLogger.Info($"Mod butonu: {PlcTags.Mode} = {value} ({text}) yazılıyor.");

            try
            {
                await _writeQueue.Enqueue(
                    () => _plcService.WriteIntTag(PlcTags.Mode, value));

                ShowSystemMessage($"Mod komutu gönderildi: {text}", isError: false);
            }
            catch (Exception ex)
            {
                string error = FriendlyPlcError(ex);
                AppLogger.Error($"{PlcTags.Mode} = {value} ({text}) yazılamadı", ex);
                ShowSystemMessage($"Mod değiştirilemedi ({text}): {error}", isError: true);
            }
            finally
            {
                ModeButtonsPanel.IsEnabled = true;
            }
        }

        // PLC'den okunan moda karşılık gelen butonu vurgular.
        // null: bağlantı yok, hiçbir buton vurgulanmaz.
        private void HighlightModeButton(short? mode)
        {
            ModeKapaliButton.Tag = mode == MachineModes.Kapali ? "Active" : null;
            ModeGuvenliButton.Tag = mode == MachineModes.Guvenli ? "Active" : null;
            ModeManuelButton.Tag = mode == MachineModes.Manuel ? "Active" : null;
            ModeOtomatikButton.Tag = mode == MachineModes.Otomatik ? "Active" : null;
        }

        // ---------------- Bağlantı göstergeleri ----------------

        private void ShowPlcConnected(short mode)
        {
            MachineStateCard.ToolTip = null;

            ModeText.Text = MachineModes.ToText(mode);
            ModeText.SetResourceReference(
                TextBlock.ForegroundProperty, ModeColorKey(mode) ?? "PrimaryText");
            HighlightModeButton(mode);

            SetFooterPlcStatus($"PLC {_plcService.IpAddress}  ·  Bağlı", "OkColor", "SecondaryText");

            // Yalnızca durum değiştiğinde logla; her 500 ms'de değil.
            if (_plcConnected != true)
            {
                _plcConnected = true;
                AppLogger.Info($"PLC bağlantısı kuruldu ({_plcService.IpAddress}).");
                ShowSystemMessage("PLC bağlantısı kuruldu.", isError: false);
            }

            if (_lastMode != mode)
            {
                _lastMode = mode;
                AppLogger.Info($"Çalışma modu: {MachineModes.ToText(mode)} (Mode = {mode}).");
            }
        }

        private void ShowPlcDisconnected(Exception ex)
        {
            string error = FriendlyPlcError(ex);

            MachineStateCard.ToolTip = error;

            // Bağlantı yokken son bilinen modu / çalışma durumunu göstermek
            // yanıltıcı olur.
            ModeText.Text = "-";
            ModeText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            HighlightModeButton(null);
            _lastMode = null;
            _autoCycle = null;

            SetFooterPlcStatus($"PLC {_plcService.IpAddress}  ·  Bağlantı yok", "ErrorColor", "ErrorColor");

            // Yalnızca durum değiştiğinde logla; bağlantı yokken her
            // denemede aynı hata dosyaya yazılmaz.
            if (_plcConnected != false)
            {
                _plcConnected = false;
                AppLogger.Warn($"PLC bağlantısı yok ({_plcService.IpAddress}): {error} [{ex.GetType().Name}: {ex.Message}]");
                ShowSystemMessage("PLC bağlantı hatası: " + error, isError: true);
            }
        }

        // ---------------- Makine durumu ----------------

        // Öncelik: Bağlantı yok > Arıza Var > Çalışıyor > Hazır.
        //   Arıza: Alm dizisinde en az bir aktif alarm.
        //   Çalışıyor: AutoCycle = TRUE.
        // Kenarlık: arızada kırmızı; aksi hâlde Manuel turuncu,
        // Otomatik mavi, diğer modlarda varsayılan.
        // ---------------- USB ışıklı buton modülü ----------------

        private void StartEzSwitch()
        {
            try
            {
                _ezSwitch.ConnectionChanged += EzSwitch_ConnectionChanged;
                _ezSwitch.ButtonChanged += EzSwitch_ButtonChanged;
                _ezSwitch.Start();
            }
            catch (Exception ex)
            {
                // Buton modülü olmadan da program çalışmaya devam etmeli.
                AppLogger.Error("EZ buton modülü başlatılamadı", ex);
            }
        }

        // Kapanışta animasyon durdurulur, lambalar söndürülür
        // (program kapalıyken lambaların yanık kalmaması için).
        private void StopEzSwitch()
        {
            _ezCts.Cancel();

            try { _ezAnimation?.Wait(300); } catch { }

            try { _ezSwitch.AllOff(); } catch { }
            _ezSwitch.Dispose();
        }

        // Arka plan thread'inden gelir → UI thread'ine aktarılır.
        private void EzSwitch_ConnectionChanged(bool connected)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_ezCts.IsCancellationRequested)
                    return;

                if (!connected)
                {
                    ShowSystemMessage("Buton modülü bağlantısı koptu.", isError: true);
                    return;
                }

                ShowSystemMessage("Buton modülü bağlandı.", isError: false);

                // Program açılışında (ilk bağlantıda) gökkuşağı, sonra beyaz.
                // Sonraki bağlantılarda (çıkar/tak) doğrudan beyaz.
                if (!_ezStartupPlayed)
                {
                    _ezStartupPlayed = true;
                    _ezAnimation = Task.Run(() =>
                        EzSwitchAnimations.PlayStartupAsync(_ezSwitch, _ezCts.Token));
                }
                else if (_ezAnimation == null || _ezAnimation.IsCompleted)
                {
                    _ezSwitch.SetAll(255, 255, 255, LampMode.Sabit);
                }
            }));
        }

        // Buton olayları. Butonlara henüz görev atanmadı; yalnızca loglanır.
        // Görev atanırken: bu modül güvenlik fonksiyonu için KULLANILMAZ ve
        // PLC'ye yazan bir görev açık operatör eylemi olarak tasarlanmalıdır.
        private void EzSwitch_ButtonChanged(int button, bool pressed)
        {
            AppLogger.Info($"EZ buton {button} {(pressed ? "basıldı" : "bırakıldı")}");
        }

        // ---------------- Alt çubuk ----------------

        // Sağdaki sabit PLC durumu: renkli nokta + metin.
        private void SetFooterPlcStatus(string text, string dotColorKey, string textColorKey)
        {
            StatusBarPlcText.Text = text;
            StatusBarPlcText.SetResourceReference(TextBlock.ForegroundProperty, textColorKey);
            StatusBarPlcDot.SetResourceReference(Shape.FillProperty, dotColorKey);
        }

        // Aktif arıza varsa sol alanda sistem mesajı yerine arıza
        // açıklamalarını sırayla gösterir: [ARIZA 2/3]  [12] Açıklama
        // advance = true: bir sonraki arızaya geç (saniyelik zamanlayıcı).
        // PLC bağlantısı yokken alarm durumu bilinmediği için akış gizlenir.
        private void UpdateAlarmTicker(bool advance)
        {
            var alarms = _alarmService.ActiveAlarms;
            int count = alarms.Count;

            if (count == 0 || _plcConnected != true)
            {
                _alarmTickerIndex = 0;
                AlarmTickerPanel.Visibility = Visibility.Collapsed;
                StatusBarMessageText.Visibility = Visibility.Visible;
                return;
            }

            if (advance)
                _alarmTickerIndex++;

            if (_alarmTickerIndex >= count)
                _alarmTickerIndex = 0;

            ActiveAlarm alarm = alarms[_alarmTickerIndex];

            AlarmTickerBadge.Text = count == 1
                ? "ARIZA"
                : $"ARIZA {_alarmTickerIndex + 1}/{count}";
            AlarmTickerText.Text = $"[{alarm.No}] {alarm.Metin}";
            AlarmTickerText.ToolTip = $"{alarm.BaslangicText}  [{alarm.No}] {alarm.Metin}";

            StatusBarMessageText.Visibility = Visibility.Collapsed;
            AlarmTickerPanel.Visibility = Visibility.Visible;
        }

        private void UpdateMachineState()
        {
            string text;
            string textColor;
            string borderColor = null;   // null: varsayılan kenarlık

            int alarmCount = _alarmService.ActiveAlarms.Count;

            if (_plcConnected != true)
            {
                text = _plcConnected == null ? "● Bağlantı bekleniyor" : "● Bağlantı yok";
                textColor = _plcConnected == null ? "WarningColor" : "ErrorColor";
                borderColor = _plcConnected == null ? null : "ErrorColor";
            }
            else if (alarmCount > 0)
            {
                text = $"● Arıza Var ({alarmCount})";
                textColor = "ErrorColor";
                borderColor = "ErrorColor";
            }
            else
            {
                if (_autoCycle == true)
                {
                    text = "● Çalışıyor";
                    textColor = "ModeOtomatikColor";
                }
                else
                {
                    text = "● Hazır";
                    textColor = "OkColor";
                }

                if (_lastMode == MachineModes.Manuel)
                    borderColor = "ModeManuelColor";
                else if (_lastMode == MachineModes.Otomatik)
                    borderColor = "ModeOtomatikColor";
            }

            MachineStateText.Text = text;
            MachineStateText.SetResourceReference(TextBlock.ForegroundProperty, textColor);

            // Renkli kenarlık 2 px; kartın dış ölçüsü değişmesin diye iç
            // boşluk 1 px azaltılır.
            if (borderColor == null)
            {
                MachineStateCard.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
                MachineStateCard.BorderThickness = new Thickness(1);
                MachineStateCard.Padding = new Thickness(10, 7, 10, 7);
            }
            else
            {
                MachineStateCard.SetResourceReference(Border.BorderBrushProperty, borderColor);
                MachineStateCard.BorderThickness = new Thickness(2);
                MachineStateCard.Padding = new Thickness(9, 6, 9, 6);
            }

            // Yalnızca durum değiştiğinde logla (arıza sayısı hariç).
            string stateKey = text.StartsWith("● Arıza") ? "● Arıza Var" : text;

            if (_machineState != stateKey)
            {
                _machineState = stateKey;
                AppLogger.Info($"Makine durumu: {stateKey.TrimStart('●', ' ')}");
            }
        }

        // Moda karşılık gelen tema rengi anahtarı; bilinmeyen mod için null.
        private static string ModeColorKey(short mode)
        {
            switch (mode)
            {
                case MachineModes.Kapali: return "ModeKapaliColor";
                case MachineModes.Guvenli: return "ModeGuvenliColor";
                case MachineModes.Manuel: return "ModeManuelColor";
                case MachineModes.Otomatik: return "ModeOtomatikColor";
                default: return null;
            }
        }

        // Alt durum çubuğunda saatli mesaj gösterir. Herhangi bir thread'den
        // çağrılabilir; gerekirse UI thread'ine aktarılır.
        public void ShowSystemMessage(string message, bool isError)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(
                    () => ShowSystemMessage(message, isError)));
                return;
            }

            StatusBarMessageText.Text = $"{DateTime.Now:HH:mm:ss}  {message}";
            StatusBarMessageText.ToolTip = message;
            StatusBarMessageText.SetResourceReference(
                TextBlock.ForegroundProperty,
                isError ? "ErrorColor" : "SecondaryText");
        }

        // libplctag durum kodlarını operatörün anlayacağı metne çevirir.
        private static string FriendlyPlcError(Exception ex)
        {
            switch (ex.Message)
            {
                case "ErrorTimeout":
                    return "PLC yanıt vermiyor (zaman aşımı)";
                case "ErrorNotFound":
                    return "PLC'de tag bulunamadı";
                case "ErrorBadConnection":
                case "ErrorBadGateway":
                    return "PLC ile bağlantı kurulamadı";
                default:
                    return ex.Message;
            }
        }

        // ---------------- Tema ve menü ----------------

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

            // Koyu temada güneş (açığa geç), açık temada ay (koyuya geç).
            ThemeSunIcon.Visibility = _isDarkTheme ? Visibility.Visible : Visibility.Collapsed;
            ThemeMoonIcon.Visibility = _isDarkTheme ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---------------- Üst menü ve sayfa geçişleri ----------------

        // Menü butonları: sayfa anahtarı CommandParameter'da
        // (Tag, seçili görünüm için "Active" olarak kullanılır).
        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is string key)
                NavigateTo(key);
        }

        // Bölüm sayfalarında (AYARLAR, ÜRETİM) son seçilen alt sayfa;
        // sekmeye geri dönülünce aynı alt sayfa açılır.
        private readonly Dictionary<string, string> _lastSubPage =
            new Dictionary<string, string>();

        // Sayfa anahtarına göre içeriği değiştirir ve menüde ilgili
        // butonu seçili gösterir.
        //   Otomatik, Ayarlar, Uretim, Arizalar, Genel
        //   GelismisAyarlar, CanliIzleme: Genel içinden açılır (menüde GENEL seçili).
        private void NavigateTo(string key)
        {
            switch (key)
            {
                case "Otomatik":
                    MainFrame.Navigate(new Pages.OtomatikPage());
                    SetActiveMenu(MenuOtomatikButton);
                    break;

                case "Ayarlar":
                    MainFrame.Navigate(CreateSection("Ayarlar", AyarlarItems()));
                    SetActiveMenu(MenuAyarlarButton);
                    break;

                case "Uretim":
                    MainFrame.Navigate(CreateSection("Uretim", UretimItems()));
                    SetActiveMenu(MenuUretimButton);
                    break;

                case "Arizalar":
                    MainFrame.Navigate(new Pages.AlarmGecmisiPage(
                        _alarmService.History, ShowActiveAlarms));
                    SetActiveMenu(MenuArizalarButton);
                    break;

                case "Genel":
                    MainFrame.Navigate(new Pages.GenelPage(
                        () => NavigateTo("GelismisAyarlar"),
                        () => NavigateTo("CanliIzleme")));
                    SetActiveMenu(MenuGenelButton);
                    break;

                case "GelismisAyarlar":
                    MainFrame.Navigate(new Pages.PlaceholderPage(
                        "Gelişmiş Ayarlar", "Servo tork limitleri"));
                    SetActiveMenu(MenuGenelButton);
                    break;

                case "CanliIzleme":
                    MainFrame.Navigate(new Pages.CanliIzlemePage(_plcService));
                    SetActiveMenu(MenuGenelButton);
                    break;

                default:
                    AppLogger.Warn($"Bilinmeyen sayfa anahtarı: {key}");
                    break;
            }
        }

        private Pages.SectionPage CreateSection(string sectionKey, IList<Pages.SectionItem> items)
        {
            _lastSubPage.TryGetValue(sectionKey, out string last);

            return new Pages.SectionPage(items, last,
                selected => _lastSubPage[sectionKey] = selected);
        }

        // AYARLAR: istasyon sayfaları (parametreler + manuel butonlar).
        // İçerikleri sırayla doldurulacak; şimdilik yer tutucu.
        private static IList<Pages.SectionItem> AyarlarItems()
        {
            return new[]
            {
                Station("VibrasyonMerkezleme", "Vibrasyon Merkezleme"),
                Station("TransferYukleme", "Transfer Yükleme"),
                Station("DonerTabla", "Döner Tabla"),
                Station("GirisCikisBomse", "Giriş Çıkış Bomse"),
                Station("CapakKirma", "Çapak Kırma"),
                Station("Zimpara1", "Zımpara-1"),
                Station("Zimpara2", "Zımpara-2"),
                Station("Sunger1", "Sünger-1"),
                Station("Sunger2", "Sünger-2"),
                Station("Sunger3", "Sünger-3"),
                Station("TransferBosaltma", "Transfer Boşaltma"),
                Station("CikisKonveyor", "Çıkış Konveyör")
            };
        }

        // ÜRETİM: form, reçete ve servo ayarları.
        private static IList<Pages.SectionItem> UretimItems()
        {
            return new[]
            {
                new Pages.SectionItem("Form", "Form",
                    () => new Pages.PlaceholderPage("Form", "Tabak formu seçimi ve düzenleme")),
                new Pages.SectionItem("Recete", "Reçete",
                    () => new Pages.PlaceholderPage("Reçete", "Reçete yükleme ve düzenleme")),
                new Pages.SectionItem("Servo", "Servo",
                    () => new Pages.PlaceholderPage("Servo", "Servo üretim ayarları"))
            };
        }

        private static Pages.SectionItem Station(string key, string title)
        {
            return new Pages.SectionItem(key, title,
                () => new Pages.PlaceholderPage(title, "İstasyon ayarları ve manuel kumanda"));
        }

        private void SetActiveMenu(Button active)
        {
            foreach (Button b in new[]
            {
                MenuOtomatikButton, MenuAyarlarButton, MenuUretimButton,
                MenuArizalarButton, MenuGenelButton
            })
            {
                b.Tag = b == active ? "Active" : null;
            }
        }
    }
}
