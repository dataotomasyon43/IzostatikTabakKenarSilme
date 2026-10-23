using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace IzostatikTabakKenarSilme.Services
{
    // Geçmişteki tek bir alarm olayı.
    [DataContract]
    public sealed class AlarmRecord
    {
        public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        public const string StateRaised = "Geldi";
        public const string StateCleared = "Gitti";

        [DataMember(Order = 1)]
        public string Zaman { get; set; }

        [DataMember(Order = 2)]
        public int No { get; set; }

        [DataMember(Order = 3)]
        public string Alarm { get; set; }

        [DataMember(Order = 4)]
        public string Durum { get; set; }

        public DateTime Time =>
            DateTime.TryParseExact(Zaman, TimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime t)
                ? t
                : DateTime.MinValue;

        public static AlarmRecord Create(DateTime time, int no, string text, string state)
        {
            return new AlarmRecord
            {
                Zaman = time.ToString(TimeFormat, CultureInfo.InvariantCulture),
                No = no,
                Alarm = text,
                Durum = state
            };
        }
    }

    // Alarm geçmişini uygulama dizininde, aylık JSON Lines dosyalarında
    // saklar:  <uygulama dizini>\AlarmGecmisi\2026-10.jsonl
    // Her satır bir olaydır; dosya yalnızca sonuna eklenerek büyür, bu
    // yüzden elektrik kesintisinde en fazla son satır bozulur. Okunamayan
    // satırlar atlanır.
    public sealed class AlarmHistoryStore
    {
        private const int KeepMonths = 12;

        private readonly object _sync = new object();

        private readonly DataContractJsonSerializer _serializer =
            new DataContractJsonSerializer(typeof(AlarmRecord));

        public string HistoryDirectory { get; } =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AlarmGecmisi");

        public void Append(IEnumerable<AlarmRecord> records)
        {
            try
            {
                lock (_sync)
                {
                    Directory.CreateDirectory(HistoryDirectory);

                    foreach (var group in records.GroupBy(r => FileFor(r.Time)))
                    {
                        var sb = new StringBuilder();

                        foreach (AlarmRecord record in group)
                            sb.AppendLine(Serialize(record));

                        File.AppendAllText(group.Key, sb.ToString(), new UTF8Encoding(false));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Alarm geçmişi yazılamadı ({HistoryDirectory})", ex);
            }
        }

        // [from, to) aralığındaki kayıtları en yeniden eskiye döndürür.
        public List<AlarmRecord> Load(DateTime from, DateTime to)
        {
            var result = new List<AlarmRecord>();

            try
            {
                lock (_sync)
                {
                    if (!Directory.Exists(HistoryDirectory))
                        return result;

                    var month = new DateTime(from.Year, from.Month, 1);

                    while (month < to)
                    {
                        string file = FileFor(month);

                        if (File.Exists(file))
                        {
                            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                            {
                                AlarmRecord record = Deserialize(line);

                                if (record == null)
                                    continue;

                                DateTime t = record.Time;

                                if (t >= from && t < to)
                                    result.Add(record);
                            }
                        }

                        month = month.AddMonths(1);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Alarm geçmişi okunamadı ({HistoryDirectory})", ex);
            }

            return result
                .OrderByDescending(r => r.Time)
                .ToList();
        }

        // KeepMonths aydan eski dosyaları siler.
        public void DeleteOldFiles()
        {
            try
            {
                lock (_sync)
                {
                    if (!Directory.Exists(HistoryDirectory))
                        return;

                    DateTime now = DateTime.Now;
                    DateTime limit = new DateTime(now.Year, now.Month, 1).AddMonths(-KeepMonths);

                    foreach (string file in Directory.GetFiles(HistoryDirectory, "*.jsonl"))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);

                        if (DateTime.TryParseExact(name, "yyyy-MM", CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out DateTime month)
                            && month < limit)
                        {
                            File.Delete(file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Eski alarm geçmişi dosyaları silinemedi", ex);
            }
        }

        private string FileFor(DateTime time)
        {
            return Path.Combine(HistoryDirectory,
                time.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".jsonl");
        }

        private string Serialize(AlarmRecord record)
        {
            using (var ms = new MemoryStream())
            {
                _serializer.WriteObject(ms, record);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private AlarmRecord Deserialize(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                    return (AlarmRecord)_serializer.ReadObject(ms);
            }
            catch
            {
                // Bozuk satır (ör. kesinti sırasında yarım yazılmış): atla.
                return null;
            }
        }
    }
}
