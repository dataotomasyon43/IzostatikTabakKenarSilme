using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Windows
{
    // Aktif alarmları gösteren açılır pencere.
    //
    // ALARM RESET butonu "push button" gibi çalışır:
    //   basıldığında  → Mode = 1 (Güvenli), ardından HMI_Reset = TRUE
    //   bırakıldığında→ HMI_Reset = FALSE
    //
    // PLC (Genel_Prog rung 1): (Mode = 1 VEYA Mode = 3) VE HMI_Reset →
    // TP 250 ms → Reset (+ inverter / güvenlik rölesi / EC-MC-PLC hata
    // resetleri). Manuel ve Kapalı modda HMI_Reset tek başına reset
    // yapmadığı için önce Güvenli moda geçilir.
    // Not: 23 ve 58–71 numaralı alarmlar doğrudan HMI_Reset ile silinir;
    // HMI_Reset TRUE kalırsa bu alarmlar mühürlenemez → FALSE yazması
    // birkaç kez denenir.
    // Butonun bırakılmış sayıldığı durumlar: parmak/fare kaldırıldı,
    // yakalama kayboldu (dokunma iptali vb.), pencere kapandı.
    // Yazmalar PlcWriteQueue ile sırayla gider; FALSE asla TRUE'dan
    // önce gönderilmez.
    public partial class AktifAlarmlarWindow : Window
    {
        private const int ReleaseRetryCount = 3;

        private readonly PlcService _plc;
        private readonly PlcWriteQueue _writeQueue;
        private readonly AlarmService _alarms;

        private bool _resetPressed;

        public AktifAlarmlarWindow(PlcService plc, PlcWriteQueue writeQueue, AlarmService alarms)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _writeQueue = writeQueue ?? throw new ArgumentNullException(nameof(writeQueue));
            _alarms = alarms ?? throw new ArgumentNullException(nameof(alarms));

            InitializeComponent();

            AlarmList.ItemsSource = _alarms.ActiveAlarms;
            _alarms.ActiveAlarms.CollectionChanged += ActiveAlarms_CollectionChanged;

            Closing += AktifAlarmlarWindow_Closing;
            Closed += AktifAlarmlarWindow_Closed;

            UpdateCount();
        }

        private void ActiveAlarms_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateCount();
        }

        private void UpdateCount()
        {
            int count = _alarms.ActiveAlarms.Count;

            CountText.Text = $"({count})";
            EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AktifAlarmlarWindow_Closing(object sender, CancelEventArgs e)
        {
            // Pencere kapanırken buton basılıysa reset FALSE'a çekilir.
            ReleaseReset();
        }

        private void AktifAlarmlarWindow_Closed(object sender, EventArgs e)
        {
            _alarms.ActiveAlarms.CollectionChanged -= ActiveAlarms_CollectionChanged;
        }

        // ---------------- Fare ----------------

        private void ResetButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ResetButton.CaptureMouse();
            PressReset();
            e.Handled = true;
        }

        private void ResetButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ReleaseReset();

            if (ResetButton.IsMouseCaptured)
                ResetButton.ReleaseMouseCapture();

            e.Handled = true;
        }

        private void ResetButton_LostMouseCapture(object sender, MouseEventArgs e)
        {
            ReleaseReset();
        }

        // ---------------- Dokunmatik ----------------
        // e.Handled = true: dokunma fare olayına dönüştürülmez, böylece
        // aynı basma iki kez işlenmez.

        private void ResetButton_TouchDown(object sender, TouchEventArgs e)
        {
            ResetButton.CaptureTouch(e.TouchDevice);
            PressReset();
            e.Handled = true;
        }

        private void ResetButton_TouchUp(object sender, TouchEventArgs e)
        {
            ReleaseReset();
            ResetButton.ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        private void ResetButton_LostTouchCapture(object sender, TouchEventArgs e)
        {
            ReleaseReset();
        }

        // ---------------- Reset mantığı ----------------

        private async void PressReset()
        {
            if (_resetPressed)
                return;

            _resetPressed = true;
            ShowPressed(true);
            ShowStatus("Reset gönderiliyor...", isError: false);

            AppLogger.Info($"Alarm reset basıldı: {PlcTags.Mode} = {MachineModes.Guvenli} (Güvenli), {PlcTags.Reset} = TRUE");

            try
            {
                await _writeQueue.Enqueue(() =>
                {
                    _plc.WriteIntTag(PlcTags.Mode, MachineModes.Guvenli);
                    _plc.WriteBoolTag(PlcTags.Reset, true);
                });

                // Kullanıcı bu arada bıraktıysa mesajı ReleaseReset verir.
                if (_resetPressed)
                    ShowStatus("Reset aktif — bırakınca sonlanır.", isError: false);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Alarm reset yazılamadı (Mode = 1 / HMI_Reset = TRUE)", ex);
                ShowStatus("Reset gönderilemedi: " + ex.Message, isError: true);
            }
        }

        private async void ReleaseReset()
        {
            if (!_resetPressed)
                return;

            _resetPressed = false;
            ShowPressed(false);

            AppLogger.Info($"Alarm reset bırakıldı: {PlcTags.Reset} = FALSE");

            try
            {
                // FALSE yazması önemli: başarısız olursa birkaç kez denenir.
                await _writeQueue.Enqueue(() =>
                {
                    Exception last = null;

                    for (int attempt = 1; attempt <= ReleaseRetryCount; attempt++)
                    {
                        try
                        {
                            _plc.WriteBoolTag(PlcTags.Reset, false);
                            return;
                        }
                        catch (ObjectDisposedException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            last = ex;
                            AppLogger.Warn($"{PlcTags.Reset} = FALSE yazılamadı (deneme {attempt}/{ReleaseRetryCount}): {ex.Message}");
                            Thread.Sleep(300);
                        }
                    }

                    throw last;
                });

                ShowStatus("Reset tamamlandı.", isError: false);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"{PlcTags.Reset} FALSE yazılamadı — PLC'de TRUE kalmış olabilir!", ex);
                ShowStatus($"DİKKAT: {PlcTags.Reset} sıfırlanamadı, PLC'de TRUE kalmış olabilir! ({ex.Message})", isError: true);
            }
        }

        private void ShowPressed(bool pressed)
        {
            ResetButton.SetResourceReference(
                Border.BackgroundProperty,
                pressed ? "ErrorColor" : "AccentColor");

            ResetButtonText.Text = pressed ? "RESET AKTİF" : "ALARM RESET";
        }

        private void ShowStatus(string message, bool isError)
        {
            // Pencere kapandıktan sonra gelen sonuçlar yalnızca loglanır.
            if (!IsLoaded)
                return;

            StatusText.Text = $"{DateTime.Now:HH:mm:ss}  {message}";
            StatusText.SetResourceReference(
                TextBlock.ForegroundProperty,
                isError ? "ErrorColor" : "SecondaryText");
        }
    }
}
