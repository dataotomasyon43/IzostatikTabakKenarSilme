using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// Otomatik: çevrim Start/Stop ve üretim adetleri.
    ///
    /// Veri:
    ///   - Çevrim durumu, mod, bağlantı → MachineState (ana pencere okur;
    ///     bu sayfa tekrar okumaz).
    ///   - Vardiya / toplam adet → bu sayfanın kendi okuma döngüsü
    ///     (yalnızca sayfa açıkken, PagePoller).
    /// Komutlar (basılı tutma): Start, Stop, Vardiya Sıfırla → PlcCommands.
    /// </summary>
    public partial class OtomatikPage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;

        // Okuma hatası yalnızca değişimde gösterilsin/loglansın.
        private bool? _countsReadOk;

        // Boşaltma bitlerinin PLC'den son okunan değeri (null: bilinmiyor).
        private bool? _bosaltmaModu;
        private bool? _bosaltmaBasla;

        // Yazması süren aç/kapa butonları (çift basmada iki yazma yarışmasın).
        private readonly HashSet<Button> _busyToggles = new HashSet<Button>();

        // settingsItems: sağdaki kısayol menüsü (AYARLAR alt sayfaları).
        // openSetting: kısayola basılınca çağrılır; AYARLAR sekmesini ilgili
        // alt sayfayla açar (ana pencere).
        public OtomatikPage(PlcService plc, PlcCommands commands, MachineState state,
            IEnumerable<SectionItem> settingsItems, Action<string> openSetting)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent();

            BuildSettingsMenu(settingsItems, openSetting);

            // Boşaltma butonları ilk okuma gelene kadar devre dışı.
            SetBosaltmaState(null, null);

            // Sayfa açıkken adetleri ve boşaltma bitlerini oku.
            new PagePoller(this, PollInterval, PollCountsAsync, OnCountsReadError);

            Loaded += (s, e) =>
            {
                _state.PropertyChanged += State_PropertyChanged;
                UpdateFromState();
            };

            // MachineState uygulama boyunca yaşar: abonelik bırakılmazsa
            // sayfa bellekte kalır.
            Unloaded += (s, e) => _state.PropertyChanged -= State_PropertyChanged;

            StartButton.Pressed += (s, e) => Run(_commands.PressStartAsync(), "Start");
            StartButton.Released += (s, e) => Run(_commands.ReleaseStartAsync(), "Start bırak");

            StopButton.Pressed += (s, e) => Run(_commands.PressStopAsync(), "Stop");
            StopButton.Released += (s, e) => Run(_commands.ReleaseStopAsync(), "Stop bırak");

            ShiftResetButton.Pressed += (s, e) => Run(_commands.PressShiftResetAsync(), "Vardiya sıfırla");
            ShiftResetButton.Released += (s, e) => Run(_commands.ReleaseShiftResetAsync(), "Vardiya sıfırla bırak");
        }

        // ---------------- AYARLAR kısayolları ----------------

        // AYARLAR sayfasındaki alt menünün aynısı (aynı stil ve satır
        // düzeni); burada seçili öğe yok, basınca AYARLAR'a geçilir.
        private void BuildSettingsMenu(IEnumerable<SectionItem> items, Action<string> openSetting)
        {
            if (items == null || openSetting == null)
                return;

            var style = (Style)FindResource("SubMenuButtonStyle");
            int count = 0;

            foreach (SectionItem item in items)
            {
                string key = item.Key;
                var button = new Button { Content = item.Title, Style = style };
                button.Click += (s, e) => openSetting(key);
                SettingsMenuPanel.Children.Add(button);
                count++;
            }

            SettingsMenuPanel.Rows = Math.Max(count, SectionPage.MinRows);
        }

        // ---------------- Makine durumu (ana pencereden) ----------------

        private void State_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            UpdateFromState();
        }

        private void UpdateFromState()
        {
            bool connected = _state.IsConnected;
            bool running = connected && _state.AutoCycle == true;
            bool autoMode = connected && _state.Mode == MachineModes.Otomatik;

            if (!connected)
                SetCycleState("● Bağlantı yok", "ErrorColor");
            else if (running)
                SetCycleState("● Çalışıyor", "ModeOtomatikColor");
            else
                SetCycleState("● Durdu", "SecondaryText");

            // Çalışırken START butonu yanık (aktif) görünür.
            StartButton.Tag = running ? "Active" : null;

            // Start yalnızca Otomatik modda anlamlı (PLC: AutoCycle için
            // Otomatik gerekir). Devre dışı kalırken basılıysa HoldButton
            // otomatik "bırakıldı" verir.
            StartButton.IsEnabled = autoMode;
            StopButton.IsEnabled = connected;
            ShiftResetButton.IsEnabled = connected;

            StartHintText.Visibility = connected && !autoMode
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SetCycleState(string text, string colorKey)
        {
            CycleStateText.Text = text;
            CycleStateText.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
        }

        // ---------------- Sayfa okuması: adetler ----------------

        private async Task PollCountsAsync(CancellationToken ct)
        {
            // Bağlantı yokken okumayı deneme (zaman aşımlarıyla haberleşmeyi
            // yormasın); ana pencere bağlantıyı zaten izliyor.
            if (!_state.IsConnected)
            {
                ShiftCountText.Text = "—";
                TotalCountText.Text = "—";
                SetBosaltmaState(null, null);
                return;
            }

            var data = await Task.Run(() =>
            {
                short shift = _plc.ReadIntTag(PlcTags.VardiyaAdet);
                uint total = _plc.ReadUDintTag(PlcTags.ToplamAdet);
                bool modu = _plc.ReadBoolTag(PlcTags.BosaltmaModu);
                bool basla = _plc.ReadBoolTag(PlcTags.BosaltmaBasla);
                return (shift, total, modu, basla);
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            ShiftCountText.Text = data.shift.ToString("N0", Tr);
            TotalCountText.Text = data.total.ToString("N0", Tr);
            SetBosaltmaState(data.modu, data.basla);

            if (_countsReadOk == false)
                ShowStatus(string.Empty, isError: false);

            _countsReadOk = true;
        }

        private void OnCountsReadError(Exception ex)
        {
            ShiftCountText.Text = "—";
            TotalCountText.Text = "—";
            SetBosaltmaState(null, null);

            if (_countsReadOk != false)
            {
                _countsReadOk = false;
                AppLogger.Warn($"Otomatik sayfası: adetler okunamadı: {ex.Message}");
                ShowStatus("Adetler okunamadı: " + ex.Message, isError: true);
            }
        }

        // ---------------- Boşaltma (kalıcı aç/kapa) ----------------

        // PLC'den okunan durumu gösterir: TRUE → yeşil ve aktif; FALSE →
        // tema varsayılanı; bilinmiyor (null) → buton devre dışı.
        private void SetBosaltmaState(bool? modu, bool? basla)
        {
            _bosaltmaModu = modu;
            _bosaltmaBasla = basla;

            SetToggleVisual(BosaltmaModuButton, modu);
            SetToggleVisual(BosaltmaBaslaButton, basla);
        }

        private void SetToggleVisual(Button button, bool? on)
        {
            if (on == true)
            {
                button.SetResourceReference(ForegroundProperty, "OkColor");
                button.Tag = "Active";
            }
            else
            {
                button.SetResourceReference(ForegroundProperty, "PrimaryText");
                button.Tag = null;
            }

            // Yazma sürerken buton devre dışı kalır (bkz. Click); okunan
            // durum bilinmiyorsa da basılamaz.
            if (!_busyToggles.Contains(button))
                button.IsEnabled = on.HasValue;
        }

        // Bir basış: o anki PLC değerinin tersini yazar.
        private async void BosaltmaToggle_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            bool isModu = button == BosaltmaModuButton;
            bool? current = isModu ? _bosaltmaModu : _bosaltmaBasla;

            if (!current.HasValue)
                return;

            bool target = !current.Value;
            string description = $"{(isModu ? "Boşaltma Modu" : "Boşaltma Başla")} = {(target ? "AÇIK" : "KAPALI")}";

            _busyToggles.Add(button);
            button.IsEnabled = false;

            try
            {
                await (isModu
                    ? _commands.SetBosaltmaModuAsync(target)
                    : _commands.SetBosaltmaBaslaAsync(target));

                AppLogger.Info($"Otomatik sayfası komutu: {description}");

                // Bir sonraki okuma PLC'den doğrular; o zamana kadar yeni
                // durumu göster.
                if (isModu)
                    _bosaltmaModu = target;
                else
                    _bosaltmaBasla = target;

                _busyToggles.Remove(button);
                SetToggleVisual(button, target);
            }
            catch (Exception ex)
            {
                _busyToggles.Remove(button);
                SetToggleVisual(button, current);

                AppLogger.Error($"Otomatik sayfası komutu gönderilemedi: {description}", ex);
                ShowStatus($"Komut gönderilemedi ({description}): {ex.Message}", isError: true);
            }
        }

        // ---------------- Komutlar ----------------

        private async void Run(Task command, string description)
        {
            try
            {
                await command;
                AppLogger.Info($"Otomatik sayfası komutu: {description}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Otomatik sayfası komutu gönderilemedi: {description}", ex);
                ShowStatus($"Komut gönderilemedi ({description}): {ex.Message}", isError: true);
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
