using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// Alarm geçmişi: uygulama dizinindeki AlarmGecmisi klasöründen
    /// kayıtları okur ve en yeniden eskiye listeler.
    /// </summary>
    public partial class AlarmGecmisiPage : Page
    {
        private readonly AlarmHistoryStore _history;
        private readonly Action _showActiveAlarms;

        // Seçili aralık (gün). 1 = bugün.
        private int _rangeDays = 1;

        public AlarmGecmisiPage(AlarmHistoryStore history, Action showActiveAlarms)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _showActiveAlarms = showActiveAlarms;

            InitializeComponent();

            Loaded += AlarmGecmisiPage_Loaded;
        }

        private async void AlarmGecmisiPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }

        private async void RangeButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;

            if (button == null || !int.TryParse(button.CommandParameter as string, out int days))
                return;

            _rangeDays = days;
            await LoadAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }

        private void ActiveAlarmsButton_Click(object sender, RoutedEventArgs e)
        {
            _showActiveAlarms?.Invoke();
        }

        private async Task LoadAsync()
        {
            HighlightRangeButton();
            InfoText.Text = "Yükleniyor...";

            try
            {
                DateTime to = DateTime.Today.AddDays(1);
                DateTime from = DateTime.Today.AddDays(1 - _rangeDays);

                List<AlarmRecord> records =
                    await Task.Run(() => _history.Load(from, to));

                HistoryGrid.ItemsSource = records;

                InfoText.Text = records.Count == 0
                    ? $"{RangeText()}: kayıt yok."
                    : $"{RangeText()}: {records.Count} kayıt  ({_history.HistoryDirectory})";
            }
            catch (Exception ex)
            {
                AppLogger.Error("Alarm geçmişi sayfası yüklenemedi", ex);
                InfoText.Text = "Geçmiş okunamadı: " + ex.Message;
            }
        }

        private string RangeText()
        {
            switch (_rangeDays)
            {
                case 1: return "Bugün";
                case 365: return "Son 12 ay";
                default: return $"Son {_rangeDays} gün";
            }
        }

        private void HighlightRangeButton()
        {
            foreach (Button b in new[] { TodayButton, Last7Button, Last30Button, AllButton })
            {
                b.Tag = (b.CommandParameter as string) == _rangeDays.ToString()
                    ? "Active"
                    : null;
            }
        }
    }
}
