using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace IzostatikTabakKenarSilme.Services
{
    // Şu an aktif olan bir alarm (aktif alarmlar penceresinde gösterilir).
    public sealed class ActiveAlarm
    {
        public int No { get; set; }
        public string Metin { get; set; }
        public DateTime Baslangic { get; set; }

        public string BaslangicText => Baslangic.ToString("dd.MM.yyyy HH:mm:ss");
    }

    // Alm dizisini izler: her okumayı bir öncekiyle karşılaştırır,
    // gelen (FALSE→TRUE) ve giden (TRUE→FALSE) alarmları bulur, aktif
    // alarm listesini günceller ve olayları geçmişe yazar.
    //
    // Yalnızca UI thread'inden çağrılmalıdır (ActiveAlarms arayüze bağlı).
    // PLC'ye hiçbir şey yazmaz.
    public sealed class AlarmService
    {
        private bool[] _previous;

        public AlarmTextProvider Texts { get; } = new AlarmTextProvider();
        public AlarmHistoryStore History { get; } = new AlarmHistoryStore();

        // En yeni alarm en üstte.
        public ObservableCollection<ActiveAlarm> ActiveAlarms { get; } =
            new ObservableCollection<ActiveAlarm>();

        public void Initialize()
        {
            Texts.Load();
            History.DeleteOldFiles();
        }

        // Yeni okunan alarm durumlarını işler. Yeni gelen alarm sayısını
        // döndürür (0'dan büyükse aktif alarm penceresi açılır).
        //
        // Uygulama açıldığında ilk okumada zaten aktif olan alarmlar da
        // "Geldi" olarak kaydedilir ve gösterilir.
        public int Update(bool[] current)
        {
            if (current == null)
                throw new ArgumentNullException(nameof(current));

            if (_previous == null || _previous.Length != current.Length)
                _previous = new bool[current.Length];

            DateTime now = DateTime.Now;
            var records = new List<AlarmRecord>();
            int raisedCount = 0;

            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] == _previous[i])
                    continue;

                string text = Texts.Get(i);

                if (current[i])
                {
                    raisedCount++;

                    ActiveAlarms.Insert(0, new ActiveAlarm
                    {
                        No = i,
                        Metin = text,
                        Baslangic = now
                    });

                    records.Add(AlarmRecord.Create(now, i, text, AlarmRecord.StateRaised));
                    AppLogger.Warn($"Alarm geldi: [{i}] {text}");
                }
                else
                {
                    ActiveAlarm active = ActiveAlarms.FirstOrDefault(a => a.No == i);

                    if (active != null)
                        ActiveAlarms.Remove(active);

                    records.Add(AlarmRecord.Create(now, i, text, AlarmRecord.StateCleared));
                    AppLogger.Info($"Alarm gitti: [{i}] {text}");
                }
            }

            _previous = (bool[])current.Clone();

            // Dosyaya yazma arka planda; Append kendi hatalarını loglar.
            if (records.Count > 0)
                Task.Run(() => History.Append(records));

            return raisedCount;
        }
    }
}
