using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// ARIZALAR: her arıza tek satır — başlangıç, bitiş, süre, durum.
    ///
    /// Kaynak: AlarmGecmisi klasöründeki "Geldi"/"Gitti" olayları
    /// (AlarmHistoryStore); aynı alarm numarasının "Geldi"si sonraki
    /// "Gitti"siyle eşleştirilir. Şu an aktif olanlar AlarmService'ten
    /// alınır, en üstte gösterilir ve süreleri saniyede bir güncellenir.
    /// Aktif alarm listesi değişince sayfa kendini yeniler.
    ///
    /// PLC'ye hiçbir şey yazmaz ve PLC'den okumaz (ana pencere okur).
    /// </summary>
    public partial class AlarmGecmisiPage : Page
    {
        // Seçili aralıktan önce başlayıp aralık içinde biten arızaların
        // başlangıcını bulmak için bu kadar geriye bakılır.
        private const int LookbackDays = 31;

        private readonly AlarmService _alarms;
        private readonly Action _showActiveAlarms;

        // Aktif arızaların süresi için.
        private readonly DispatcherTimer _tickTimer =
            new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        // Aktif alarm listesi değişince kısa gecikmeyle yenile (aynı anda
        // gelen birden çok alarm tek yüklemeye düşsün).
        private readonly DispatcherTimer _reloadTimer =
            new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

        // Seçili aralık (gün). 1 = bugün.
        private int _rangeDays = 1;

        // Üst üste yüklemelerde yalnız en sonuncusu ekrana yazılsın.
        private int _loadVersion;

        private List<FaultRow> _rows = new List<FaultRow>();
        private DateTime _rangeFrom;

        public AlarmGecmisiPage(AlarmService alarms, Action showActiveAlarms)
        {
            _alarms = alarms ?? throw new ArgumentNullException(nameof(alarms));
            _showActiveAlarms = showActiveAlarms;

            InitializeComponent();

            _tickTimer.Tick += (s, e) => Tick();
            _reloadTimer.Tick += async (s, e) =>
            {
                _reloadTimer.Stop();
                await LoadAsync();
            };

            Loaded += async (s, e) =>
            {
                _alarms.ActiveAlarms.CollectionChanged += ActiveAlarms_CollectionChanged;
                _tickTimer.Start();
                await LoadAsync();
            };

            // AlarmService uygulama boyunca yaşar: abonelik bırakılmazsa
            // sayfa bellekte kalır.
            Unloaded += (s, e) =>
            {
                _alarms.ActiveAlarms.CollectionChanged -= ActiveAlarms_CollectionChanged;
                _tickTimer.Stop();
                _reloadTimer.Stop();
            };
        }

        private void ActiveAlarms_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Geçmiş dosyaya arka planda yazılır; gecikme onu da bekler.
            _reloadTimer.Stop();
            _reloadTimer.Start();
        }

        // ---------------- Butonlar ----------------

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

        // Liste sonuna parmakla kaydırınca Windows'un bütün pencereyi
        // esnetmesini engeller.
        private void FaultList_ManipulationBoundaryFeedback(object sender, System.Windows.Input.ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }

        private void ActiveAlarmsButton_Click(object sender, RoutedEventArgs e)
        {
            _showActiveAlarms?.Invoke();
        }

        // ---------------- Yükleme ----------------

        private async Task LoadAsync()
        {
            int version = ++_loadVersion;

            HighlightRangeButton();
            SubtitleText.Text = $"{RangeText()} · arıza başlangıç ve bitiş kayıtları";

            DateTime to = DateTime.Today.AddDays(1);
            DateTime from = DateTime.Today.AddDays(1 - _rangeDays);

            // Aktif alarmların kopyası (UI thread'inde alınır).
            List<ActiveAlarm> active = _alarms.ActiveAlarms
                .Select(a => new ActiveAlarm { No = a.No, Metin = a.Metin, Baslangic = a.Baslangic })
                .ToList();
            AlarmHistoryStore history = _alarms.History;

            try
            {
                List<FaultRow> rows = await Task.Run(() =>
                {
                    List<AlarmRecord> records = history.Load(from.AddDays(-LookbackDays), to);
                    return FaultRow.Build(records, active, from, to);
                });

                if (version != _loadVersion)
                    return;

                _rows = rows;
                _rangeFrom = from;

                FaultList.ItemsSource = rows;
                EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                UpdateSummary();
            }
            catch (Exception ex)
            {
                if (version != _loadVersion)
                    return;

                AppLogger.Error("Arızalar sayfası yüklenemedi", ex);
                InfoText.Text = "Arıza kayıtları okunamadı: " + ex.Message;
                InfoText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorColor");
            }
        }

        // Aktif arızaların sürelerini ve özeti günceller.
        private void Tick()
        {
            DateTime now = DateTime.Now;
            bool any = false;

            foreach (FaultRow row in _rows)
            {
                if (row.IsActive)
                {
                    row.Refresh(now);
                    any = true;
                }
            }

            if (any)
                UpdateSummary();
        }

        private void UpdateSummary()
        {
            DateTime now = DateTime.Now;

            ActiveCountText.Text = _rows.Count(r => r.IsActive).ToString();
            ActiveCountText.SetResourceReference(TextBlock.ForegroundProperty,
                _rows.Any(r => r.IsActive) ? "ErrorColor" : "PrimaryText");

            TotalCountText.Text = _rows.Count.ToString("N0");
            TotalDurationText.Text = FaultRow.FormatDuration(
                FaultRow.FaultyTime(_rows, _rangeFrom, now));

            // En sık arıza
            var top = _rows
                .GroupBy(r => r.No)
                .Select(g => new { No = g.Key, Count = g.Count(), Text = g.First().Metin })
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.No)
                .FirstOrDefault();

            InfoText.Text = top == null
                ? string.Empty
                : $"En sık arıza: [{top.No}] {top.Text} — {top.Count} kez";
            InfoText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
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

    /// <summary>
    /// Arızalar listesinde bir satır: bir alarmın bir kez gelip gitmesi.
    /// </summary>
    public sealed class FaultRow : INotifyPropertyChanged
    {
        private const string TimeFormat = "dd.MM.yyyy HH:mm:ss";

        public int No { get; private set; }
        public string Metin { get; private set; }

        // null: başlangıç kaydı bakılan aralıkta yok.
        public DateTime? Baslangic { get; private set; }

        // null: hâlâ aktif ya da bitiş kaydı yok (bkz. IsUnknownEnd).
        public DateTime? Bitis { get; private set; }

        public bool IsActive { get; private set; }

        // Bitiş kaydı yok: alarm aktifken uygulama kapandı / yeniden açıldı.
        public bool IsUnknownEnd { get; private set; }

        public string DurumText =>
            IsActive ? "● AKTİF" : IsUnknownEnd ? "KAYIT YOK" : "BİTTİ";

        public string BaslangicText =>
            Baslangic.HasValue ? Baslangic.Value.ToString(TimeFormat) : "—";

        public string BitisText =>
            Bitis.HasValue ? Bitis.Value.ToString(TimeFormat) : IsActive ? "devam ediyor" : "—";

        public string SureText { get; private set; } = "—";

        public event PropertyChangedEventHandler PropertyChanged;

        // Aktif satırın süresini günceller.
        public void Refresh(DateTime now)
        {
            string text = DurationText(now);

            if (text != SureText)
            {
                SureText = text;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SureText)));
            }
        }

        private string DurationText(DateTime now)
        {
            if (!Baslangic.HasValue)
                return "—";

            if (Bitis.HasValue)
                return FormatDuration(Bitis.Value - Baslangic.Value);

            return IsActive ? FormatDuration(now - Baslangic.Value) : "—";
        }

        // 00:00:07 · 01:12:30 · 2 g 03:15:00
        public static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;

            string hms = $"{(int)span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
            return span.Days > 0 ? $"{span.Days} g {hms}" : hms;
        }

        // ---------------- Olaylardan satır oluşturma ----------------

        /// <summary>
        /// "Geldi"/"Gitti" olaylarını arıza satırlarına çevirir ve [from, to)
        /// aralığına değenleri döndürür. Sıra: aktifler üstte, sonra en
        /// yeni başlangıç en üstte.
        /// </summary>
        public static List<FaultRow> Build(
            IEnumerable<AlarmRecord> records,
            IList<ActiveAlarm> active,
            DateTime from,
            DateTime to)
        {
            DateTime now = DateTime.Now;
            var all = new List<FaultRow>();
            var open = new Dictionary<int, FaultRow>();

            // Eskiden yeniye (aynı saniyedekiler dosya sırasında kalır).
            foreach (AlarmRecord r in records.OrderBy(r => r.Time))
            {
                if (r.Durum == AlarmRecord.StateRaised)
                {
                    // Aynı alarm kapanmadan yeniden "Geldi": arada uygulama
                    // kapanıp açılmış (açılışta aktif alarmlar yeniden
                    // "Geldi" yazılır). Alarmın o arada gidip gitmediği
                    // bilinmediğinden önceki kayıt "bitiş kaydı yok" kalır;
                    // olmayan bir süre uydurulmaz.
                    if (open.TryGetValue(r.No, out FaultRow previous))
                        previous.IsUnknownEnd = true;

                    var row = new FaultRow { No = r.No, Metin = r.Alarm, Baslangic = r.Time };
                    open[r.No] = row;
                    all.Add(row);
                }
                else if (r.Durum == AlarmRecord.StateCleared)
                {
                    if (open.TryGetValue(r.No, out FaultRow row))
                    {
                        row.Bitis = r.Time;
                        open.Remove(r.No);
                    }
                    else
                    {
                        // Başlangıcı bakılan aralıktan önce.
                        all.Add(new FaultRow { No = r.No, Metin = r.Alarm, Bitis = r.Time });
                    }
                }
            }

            // Kapanmamış kayıtlar: şu an aktifse "aktif", değilse bitiş
            // kaydı yok (alarm aktifken uygulama kapanmış ve açıldığında
            // alarm gitmiş).
            foreach (FaultRow row in open.Values)
            {
                if (active.Any(a => a.No == row.No))
                    row.IsActive = true;
                else
                    row.IsUnknownEnd = true;
            }

            // Aktif olup geçmişte "Geldi" kaydı bulunamayanlar (dosyaya
            // yazma henüz bitmemiş ya da başarısız olmuş): aktif alarm
            // listesindeki başlangıçla eklenir.
            foreach (ActiveAlarm a in active)
            {
                if (!open.ContainsKey(a.No))
                {
                    all.Add(new FaultRow
                    {
                        No = a.No,
                        Metin = a.Metin,
                        Baslangic = a.Baslangic,
                        IsActive = true
                    });
                }
            }

            List<FaultRow> result = all
                .Where(r => r.Touches(from, to))
                .OrderByDescending(r => r.IsActive)
                .ThenByDescending(r => r.Baslangic ?? r.Bitis ?? DateTime.MinValue)
                .ToList();

            foreach (FaultRow row in result)
                row.SureText = row.DurationText(now);

            return result;
        }

        // Arıza [from, to) aralığına değiyor mu?
        private bool Touches(DateTime from, DateTime to)
        {
            DateTime start = Baslangic ?? Bitis.Value;
            DateTime end = Bitis ?? (IsActive ? DateTime.MaxValue : start);

            return start < to && end >= from;
        }

        /// <summary>
        /// En az bir arızanın sürdüğü toplam süre ([from, now] içinde).
        /// Aynı anda süren arızalar bir kez sayılır. Başlangıcı ya da
        /// bitişi bilinmeyenler katılmaz.
        /// </summary>
        public static TimeSpan FaultyTime(IEnumerable<FaultRow> rows, DateTime from, DateTime now)
        {
            var spans = rows
                .Where(r => r.Baslangic.HasValue && (r.Bitis.HasValue || r.IsActive))
                .Select(r => new
                {
                    Start = r.Baslangic.Value < from ? from : r.Baslangic.Value,
                    End = r.Bitis ?? now
                })
                .Where(s => s.End > s.Start)
                .OrderBy(s => s.Start)
                .ToList();

            TimeSpan total = TimeSpan.Zero;
            DateTime? curStart = null;
            DateTime curEnd = DateTime.MinValue;

            foreach (var s in spans)
            {
                if (curStart == null || s.Start > curEnd)
                {
                    if (curStart != null)
                        total += curEnd - curStart.Value;

                    curStart = s.Start;
                    curEnd = s.End;
                }
                else if (s.End > curEnd)
                {
                    curEnd = s.End;
                }
            }

            if (curStart != null)
                total += curEnd - curStart.Value;

            return total;
        }
    }
}
