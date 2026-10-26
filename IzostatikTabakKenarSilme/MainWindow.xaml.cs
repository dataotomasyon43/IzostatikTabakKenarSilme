
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

        // Operatör komutları (mod, start/stop, reset) — ekran butonları ve
        // ışıklı butonlar aynı komutları kullanır.
        private readonly PlcCommands _commands;

        // Tüm makine için gereken durum (bağlantı, mod, çevrim, alarm sayısı).
        // Bu pencerenin izleme döngüsü günceller; sayfalar buradan dinler,
        // aynı tag'leri tekrar okumaz.
        private readonly MachineState _machineState = new MachineState();

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
        private string _lastMachineStateText;  // son loglanan makine durumu metni

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

        // Işıklı butonun 5. tuşu basıldığında seçilen komut; bırakınca
        // aynı komutun FALSE'u yazılır (bu arada durum değişse bile).
        private Ez5Action _ez5Action = Ez5Action.None;

        // Lambalara son gönderilen durum (yalnızca değişince gönderilir).
        private string _ezLampState;

        public MainWindow()
        {
            _commands = new PlcCommands(_plcService, _writeQueue);

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

                    // 6) Işıklı buton lambası (5: beyaz / kırmızı / kırmızı flaş)
                    UpdateEzLamps();
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
                _alarmWindow = new AktifAlarmlarWindow(_commands, _alarmService)
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

            ModeButtonsPanel.IsEnabled = false;

            try
            {
                await RequestModeAsync(value, "Mod butonu");
            }
            finally
            {
                ModeButtonsPanel.IsEnabled = true;
            }
        }

        // Mode yazar; sonucu alt çubukta gösterir ve loglar. Hata fırlatmaz.
        // source: komutun nereden geldiği (log için).
        private async Task RequestModeAsync(short value, string source)
        {
            string text = MachineModes.ToText(value);
            AppLogger.Info($"{source}: {PlcTags.Mode} = {value} ({text}) yazılıyor.");

            try
            {
                await _commands.SetModeAsync(value);
                ShowSystemMessage($"Mod komutu gönderildi: {text}", isError: false);
            }
            catch (Exception ex)
            {
                string error = FriendlyPlcError(ex);
                AppLogger.Error($"{PlcTags.Mode} = {value} ({text}) yazılamadı", ex);
                ShowSystemMessage($"Mod değiştirilemedi ({text}): {error}", isError: true);
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

                // Program açılışında (ilk bağlantıda) gökkuşağı, ardından
                // görev renkleri. Sonraki bağlantılarda (çıkar/tak) doğrudan
                // görev renkleri.
                if (!_ezStartupPlayed)
                {
                    _ezStartupPlayed = true;
                    _ezAnimation = Task.Run(() =>
                        EzSwitchAnimations.PlayStartupAsync(_ezSwitch, _ezCts.Token));

                    _ezAnimation.ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!_ezCts.IsCancellationRequested)
                            UpdateEzLamps(force: true);
                    })));
                }
                else
                {
                    UpdateEzLamps(force: true);
                }
            }));
        }

        // ---- Işıklı buton görevleri ----
        //   1 beyaz   → Mode = 0 (Kapalı)
        //   2 yeşil   → Mode = 1 (Güvenli)
        //   3 turuncu → Mode = 2 (Manuel)
        //   4 mavi    → Mode = 3 (Otomatik)
        //   5 durumuna göre (basılı tutma):
        //       arıza var      → kırmızı flaş, RESET (Mode = 1 + HMI_Reset)
        //       çevrim çalışıyor → kırmızı sabit, STOP (HMI_Sistem_Durdur)
        //       diğer          → beyaz sabit,  START (HMI_Sistem_Baslat)
        // Bu modül güvenlik fonksiyonu (acil stop) DEĞİLDİR; STOP normal
        // çevrim durdurmadır.

        private enum Ez5Action { None, Start, Stop, Reset }

        // Arka plan thread'inden gelir → UI thread'ine aktarılır. BeginInvoke
        // sırayı korur; basma ve bırakma aynı sırayla işlenir.
        private void EzSwitch_ButtonChanged(int button, bool pressed)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_ezCts.IsCancellationRequested)
                    return;

                AppLogger.Info($"EZ buton {button} {(pressed ? "basıldı" : "bırakıldı")}");
                HandleEzButton(button, pressed);
            }));
        }

        private void HandleEzButton(int button, bool pressed)
        {
            if (button >= 1 && button <= 4)
            {
                // Mod butonları yalnızca basınca çalışır.
                if (pressed)
                    _ = RequestModeAsync((short)(button - 1), $"Işıklı buton {button}");
                return;
            }

            if (button != 5)
                return;

            if (pressed)
            {
                if (_ez5Action != Ez5Action.None)
                    return;

                _ez5Action = CurrentEz5Action();

                switch (_ez5Action)
                {
                    case Ez5Action.Reset:
                        RunEzCommand(_commands.PressResetAsync(), "Reset (Mode = 1 + HMI_Reset = TRUE)");
                        break;
                    case Ez5Action.Stop:
                        RunEzCommand(_commands.PressStopAsync(), "Stop (HMI_Sistem_Durdur = TRUE)");
                        break;
                    case Ez5Action.Start:
                        RunEzCommand(_commands.PressStartAsync(), "Start (HMI_Sistem_Baslat = TRUE)");
                        break;
                }
            }
            else
            {
                // Basarken hangi komut verildiyse onun FALSE'u yazılır.
                Ez5Action action = _ez5Action;
                _ez5Action = Ez5Action.None;

                switch (action)
                {
                    case Ez5Action.Reset:
                        RunEzCommand(_commands.ReleaseResetAsync(), "Reset bırak (HMI_Reset = FALSE)");
                        break;
                    case Ez5Action.Stop:
                        RunEzCommand(_commands.ReleaseStopAsync(), "Stop bırak (HMI_Sistem_Durdur = FALSE)");
                        break;
                    case Ez5Action.Start:
                        RunEzCommand(_commands.ReleaseStartAsync(), "Start bırak (HMI_Sistem_Baslat = FALSE)");
                        break;
                }
            }
        }

        // 5. tuşun o anki görevi: arıza > çalışıyor > beklemede.
        private Ez5Action CurrentEz5Action()
        {
            if (_plcConnected == true && _alarmService.ActiveAlarms.Count > 0)
                return Ez5Action.Reset;

            if (_plcConnected == true && _autoCycle == true)
                return Ez5Action.Stop;

            return Ez5Action.Start;
        }

        // Komutu çalıştırır; hata olursa loglar ve alt çubukta gösterir.
        private async void RunEzCommand(Task command, string description)
        {
            try
            {
                await command;
                AppLogger.Info($"Işıklı buton komutu: {description}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Işıklı buton komutu yazılamadı: {description}", ex);
                ShowSystemMessage($"Komut gönderilemedi: {description} ({FriendlyPlcError(ex)})", isError: true);
            }
        }

        // Lamba renkleri: 1–4 mod renkleri (koyu tema tonları), 5 duruma göre.
        // Yalnızca durum değişince gönderilir. Açılış animasyonu sürerken
        // dokunulmaz (animasyon bitince force ile uygulanır).
        private void UpdateEzLamps(bool force = false)
        {
            // Kapanışta lambalar söndürüldükten sonra tekrar yakılmasın.
            if (_ezCts.IsCancellationRequested)
                return;

            if (_ezAnimation != null && !_ezAnimation.IsCompleted)
                return;

            Ez5Action action = CurrentEz5Action();

            string state = action.ToString();

            if (!force && state == _ezLampState)
                return;

            _ezLampState = state;

            try
            {
                _ezSwitch.SetLamp(1, 0xF1, 0xF3, 0xF5, LampMode.Sabit, send: false); // Kapalı  #F1F3F5
                _ezSwitch.SetLamp(2, 0x00, 0xff, 0x00, LampMode.Sabit, send: false); // Güvenli #5CC872
                _ezSwitch.SetLamp(3, 0xFF, 0x72, 0x00, LampMode.Sabit, send: false); // Manuel  #F28C28
                _ezSwitch.SetLamp(4, 0x00, 0x00, 0xFF, LampMode.Sabit, send: false); // Otomatik #4DA3FF

                switch (action)
                {
                    case Ez5Action.Reset:   // arıza: kırmızı flaş (mod 0x02)
                        _ezSwitch.SetLamp(5, 0xFF, 0x00, 0x00, LampMode.Sabit, send: false);
                        break;
                    case Ez5Action.Stop:    // çalışıyor: kırmızı sabit
                        _ezSwitch.SetLamp(5, 0xFF, 0x00, 0x00, LampMode.FlasHizli, send: false);
                        break;
                    default:                // beklemede: yeşil sabit
                        _ezSwitch.SetLamp(5, 0x00, 0xFF, 0x00, LampMode.FlasHizli, send: false);
                        break;
                }

                _ezSwitch.Update();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Işıklı buton lambaları güncellenemedi", ex);
            }
        }

        // ---------------- Alt çubuk ----------------

        // Sağ alttaki PLC durumuna dokununca uygulama kapanır. Kapanışta
        // (Closing/Closed) reset FALSE yazması, lambaların söndürülmesi ve
        // PLC bağlantısının kapatılması normal şekilde yapılır.
        private void StatusBarPlcPanel_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AppLogger.Info("Uygulama alt çubuktaki PLC durumundan kapatıldı.");
            Close();
        }

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

            // Sayfalara yayınlanan ortak durum (değişmeyen değerler olay üretmez).
            _machineState.IsConnected = _plcConnected == true;
            _machineState.Mode = _plcConnected == true ? _lastMode : null;
            _machineState.AutoCycle = _plcConnected == true ? _autoCycle : null;
            _machineState.ActiveAlarmCount = alarmCount;

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

            if (_lastMachineStateText != stateKey)
            {
                _lastMachineStateText = stateKey;
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
        //   (Ayarlar, Uretim, Genel: sağda alt menülü bölüm sayfası)
        private void NavigateTo(string key)
        {
            switch (key)
            {
                case "Otomatik":
                    MainFrame.Navigate(new Pages.OtomatikPage(
                        _plcService, _commands, _machineState,
                        AyarlarItems(), OpenAyarlar));
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
                        _alarmService, ShowActiveAlarms));
                    SetActiveMenu(MenuArizalarButton);
                    break;

                case "Genel":
                    MainFrame.Navigate(CreateSection("Genel", GenelItems()));
                    SetActiveMenu(MenuGenelButton);
                    break;

                default:
                    AppLogger.Warn($"Bilinmeyen sayfa anahtarı: {key}");
                    break;
            }
        }

        // AYARLAR sekmesini verilen alt sayfayla açar (Otomatik sayfasındaki
        // kısayollar).
        private void OpenAyarlar(string subKey)
        {
            _lastSubPage["Ayarlar"] = subKey;
            NavigateTo("Ayarlar");
        }

        private Pages.SectionPage CreateSection(string sectionKey, IList<Pages.SectionItem> items)
        {
            _lastSubPage.TryGetValue(sectionKey, out string last);

            return new Pages.SectionPage(items, last,
                selected => _lastSubPage[sectionKey] = selected);
        }

        // AYARLAR: istasyon sayfaları (parametreler + manuel butonlar).
        // Doldurulmamış istasyonlar şimdilik yer tutucu.
        private IList<Pages.SectionItem> AyarlarItems()
        {
            return new[]
            {
                new Pages.SectionItem("VibrasyonMerkezleme", "Vibrasyon Merkezleme",
                    () => new Pages.VibrasyonPage(_plcService, _commands, _machineState)),
                new Pages.SectionItem("TransferYukleme", "Transfer Yükleme",
                    () => new Pages.TransferYuklemePage(_plcService, _commands, _machineState)),
                new Pages.SectionItem("DonerTabla", "Döner Tabla",
                    () => new Pages.DonerTablaPage(_plcService, _commands, _machineState)),
                new Pages.SectionItem("GirisCikisBomse", "Giriş Çıkış Bomse",
                    () => new Pages.GirisCikisBomsePage(_plcService, _commands, _machineState)),
                new Pages.SectionItem("CapakKirma", "Çapak Kırma",
                    () => new Pages.CapakKirmaPage(_plcService, _commands, _machineState)),
                new Pages.SectionItem("Zimpara1", "Zımpara-1",
                    () => new Pages.BomseIstasyonPage(_plcService, _commands, _machineState, Zimpara1Config())),
                new Pages.SectionItem("Zimpara2", "Zımpara-2",
                    () => new Pages.BomseIstasyonPage(_plcService, _commands, _machineState, Zimpara2Config())),
                new Pages.SectionItem("Sunger1", "Sünger-1",
                    () => new Pages.BomseIstasyonPage(_plcService, _commands, _machineState, Sunger1Config())),
                new Pages.SectionItem("Sunger2", "Sünger-2",
                    () => new Pages.BomseIstasyonPage(_plcService, _commands, _machineState, Sunger2Config())),
                new Pages.SectionItem("Sunger3", "Sünger-3",
                    () => new Pages.BomseIstasyonPage(_plcService, _commands, _machineState, Sunger3Config())),
                new Pages.SectionItem("TransferBosaltma", "Transfer Boşaltma",
                    () => new Pages.TransferBosaltmaPage(_plcService, _commands, _machineState)),
                new Pages.SectionItem("CikisKonveyor", "Çıkış Konveyör",
                    () => new Pages.CikisKonveyorPage(_plcService, _commands, _machineState))
            };
        }

        // ÜRETİM: form, reçete ve servo ayarları.
        private IList<Pages.SectionItem> UretimItems()
        {
            return new[]
            {
                new Pages.SectionItem("TabakYukle", "Tabak Yükle",
                    () => new Pages.TabakYuklePage(_plcService, _commands, _machineState)),
                new Pages.SectionItem("Recete", "Reçete",
                    () => new Pages.PlaceholderPage("Reçete", "Reçete yükleme ve düzenleme")),
                new Pages.SectionItem("Servo", "Servo",
                    () => new Pages.PlaceholderPage("Servo", "Servo üretim ayarları"))
            };
        }

        // GENEL: gelişmiş ayarlar ve servis (Canlı İzleme).
        private IList<Pages.SectionItem> GenelItems()
        {
            return new[]
            {
                new Pages.SectionItem("GelismisAyarlar", "Gelişmiş Ayarlar",
                    () => new Pages.GelismisAyarlarPage(_plcService, _commands, _machineState)),
                new Pages.SectionItem("CanliIzleme", "Canlı İzleme",
                    () => new Pages.CanliIzlemePage(_plcService))
            };
        }

        // Zımpara-1 (istasyon 3, Bomse 3): ortak bomse istasyonu sayfası.
        private static Pages.BomseIstasyonConfig Zimpara1Config()
        {
            return new Pages.BomseIstasyonConfig
            {
                Title = "Zımpara-1",
                Subtitle = "Bomse, taşlama ve modül ayarları, manuel kumanda",
                ImageAsset = "/Assets/Zimpara1.png",

                BomseBasPoz = PlcTags.Zim1BomseBasPoz,
                BomseAktPoz = PlcTags.Zim1BomseAktPoz,
                BitisAci = PlcTags.Zim1BitisAci,
                GeriCekilmeAci = PlcTags.Zim1ModGeriCekilmeAci,
                TurSure = PlcTags.Zim1TurSure,
                TurSayi = PlcTags.Zim1TurSayi,
                BomseYon = PlcTags.Zim1BomseYon,
                BomseJogP = PlcTags.Zim1BomseJogP,
                BomseJogN = PlcTags.Zim1BomseJogN,

                ToolHizLabel = "Taşlama Hızı",
                ToolHiz = PlcTags.Zim1TaslamaHiz,
                ToolYon = PlcTags.Zim1TaslamaYon,
                ToolStartText = "ZIMPARA DÖN",
                ToolStart = PlcTags.Zim1ManStart,

                ModHiz = PlcTags.Zim1ModHiz,
                ModAktPoz = PlcTags.Zim1ModAktPoz,
                ModBasPoz = PlcTags.Zim1ModBasPoz,
                Teach = PlcTags.Zim1Teach,
                ModJogP = PlcTags.Zim1ModJogP,
                ModJogN = PlcTags.Zim1ModJogN
            };
        }

        // Zımpara-2 (istasyon 4, Bomse 4)
        private static Pages.BomseIstasyonConfig Zimpara2Config()
        {
            return new Pages.BomseIstasyonConfig
            {
                Title = "Zımpara-2",
                Subtitle = "Bomse, taşlama ve modül ayarları, manuel kumanda",
                ImageAsset = "/Assets/Zimpara2.png",

                BomseBasPoz = PlcTags.Zim2BomseBasPoz,
                BomseAktPoz = PlcTags.Zim2BomseAktPoz,
                BitisAci = PlcTags.Zim2BitisAci,
                GeriCekilmeAci = PlcTags.Zim2ModGeriCekilmeAci,
                TurSure = PlcTags.Zim2TurSure,
                TurSayi = PlcTags.Zim2TurSayi,
                BomseYon = PlcTags.Zim2BomseYon,
                BomseJogP = PlcTags.Zim2BomseJogP,
                BomseJogN = PlcTags.Zim2BomseJogN,

                ToolHizLabel = "Taşlama Hızı",
                ToolHiz = PlcTags.Zim2TaslamaHiz,
                ToolYon = PlcTags.Zim2TaslamaYon,
                ToolStartText = "ZIMPARA DÖN",
                ToolStart = PlcTags.Zim2ManStart,

                ModHiz = PlcTags.Zim2ModHiz,
                ModAktPoz = PlcTags.Zim2ModAktPoz,
                ModBasPoz = PlcTags.Zim2ModBasPoz,
                Teach = PlcTags.Zim2Teach,
                ModJogP = PlcTags.Zim2ModJogP,
                ModJogN = PlcTags.Zim2ModJogN
            };
        }

        // Sünger-1 (istasyon 5, Bomse 5)
        private static Pages.BomseIstasyonConfig Sunger1Config()
        {
            return new Pages.BomseIstasyonConfig
            {
                Title = "Sünger-1",
                Subtitle = "Bomse, sünger ve modül ayarları, manuel kumanda",
                ImageAsset = "/Assets/Sunger1.png",

                BomseBasPoz = PlcTags.Sun1BomseBasPoz,
                BomseAktPoz = PlcTags.Sun1BomseAktPoz,
                BitisAci = PlcTags.Sun1BitisAci,
                GeriCekilmeAci = PlcTags.Sun1ModGeriCekilmeAci,
                TurSure = PlcTags.Sun1TurSure,
                TurSayi = PlcTags.Sun1TurSayi,
                BomseYon = PlcTags.Sun1BomseYon,
                BomseJogP = PlcTags.Sun1BomseJogP,
                BomseJogN = PlcTags.Sun1BomseJogN,

                ToolHizLabel = "Sünger Hızı",
                ToolHiz = PlcTags.Sun1SungerHiz,
                ToolYon = PlcTags.Sun1SungerYon,
                ToolStartText = "SÜNGER DÖN",
                ToolStart = PlcTags.Sun1ManStart,

                ModHiz = PlcTags.Sun1ModHiz,
                ModAktPoz = PlcTags.Sun1ModAktPoz,
                ModBasPoz = PlcTags.Sun1ModBasPoz,
                Teach = PlcTags.Sun1Teach,
                ModJogP = PlcTags.Sun1ModJogP,
                ModJogN = PlcTags.Sun1ModJogN
            };
        }

        // Sünger-2 (istasyon 6, Bomse 6)
        private static Pages.BomseIstasyonConfig Sunger2Config()
        {
            return new Pages.BomseIstasyonConfig
            {
                Title = "Sünger-2",
                Subtitle = "Bomse, sünger ve modül ayarları, manuel kumanda",
                ImageAsset = "/Assets/Sunger2.png",

                BomseBasPoz = PlcTags.Sun2BomseBasPoz,
                BomseAktPoz = PlcTags.Sun2BomseAktPoz,
                BitisAci = PlcTags.Sun2BitisAci,
                GeriCekilmeAci = PlcTags.Sun2ModGeriCekilmeAci,
                TurSure = PlcTags.Sun2TurSure,
                TurSayi = PlcTags.Sun2TurSayi,
                BomseYon = PlcTags.Sun2BomseYon,
                BomseJogP = PlcTags.Sun2BomseJogP,
                BomseJogN = PlcTags.Sun2BomseJogN,

                ToolHizLabel = "Sünger Hızı",
                ToolHiz = PlcTags.Sun2SungerHiz,
                ToolYon = PlcTags.Sun2SungerYon,
                ToolStartText = "SÜNGER DÖN",
                ToolStart = PlcTags.Sun2ManStart,

                ModHiz = PlcTags.Sun2ModHiz,
                ModAktPoz = PlcTags.Sun2ModAktPoz,
                ModBasPoz = PlcTags.Sun2ModBasPoz,
                Teach = PlcTags.Sun2Teach,
                ModJogP = PlcTags.Sun2ModJogP,
                ModJogN = PlcTags.Sun2ModJogN
            };
        }

        // Sünger-3 (istasyon 7, Bomse 7)
        private static Pages.BomseIstasyonConfig Sunger3Config()
        {
            return new Pages.BomseIstasyonConfig
            {
                Title = "Sünger-3",
                Subtitle = "Bomse, sünger ve modül ayarları, manuel kumanda",
                ImageAsset = "/Assets/Sunger3.png",

                BomseBasPoz = PlcTags.Sun3BomseBasPoz,
                BomseAktPoz = PlcTags.Sun3BomseAktPoz,
                BitisAci = PlcTags.Sun3BitisAci,
                GeriCekilmeAci = PlcTags.Sun3ModGeriCekilmeAci,
                TurSure = PlcTags.Sun3TurSure,
                TurSayi = PlcTags.Sun3TurSayi,
                BomseYon = PlcTags.Sun3BomseYon,
                BomseJogP = PlcTags.Sun3BomseJogP,
                BomseJogN = PlcTags.Sun3BomseJogN,

                ToolHizLabel = "Sünger Hızı",
                ToolHiz = PlcTags.Sun3SungerHiz,
                ToolYon = PlcTags.Sun3SungerYon,
                ToolStartText = "SÜNGER DÖN",
                ToolStart = PlcTags.Sun3ManStart,

                ModHiz = PlcTags.Sun3ModHiz,
                ModAktPoz = PlcTags.Sun3ModAktPoz,
                ModBasPoz = PlcTags.Sun3ModBasPoz,
                Teach = PlcTags.Sun3Teach,
                ModJogP = PlcTags.Sun3ModJogP,
                ModJogN = PlcTags.Sun3ModJogN
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
