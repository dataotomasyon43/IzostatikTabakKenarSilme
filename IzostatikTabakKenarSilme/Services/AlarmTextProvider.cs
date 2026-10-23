using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IzostatikTabakKenarSilme.Services
{
    // Alarm metinlerini uygulama dizinindeki AlarmMetinleri.csv
    // dosyasından okur. Biçim (her satır):  No;Metin
    //   0;Acil stop basılı
    //   1;Faz koruma hatası
    // '#' ile başlayan satırlar ve boş satırlar yok sayılır.
    // Metni tanımlı olmayan alarm "Alarm 37" gibi gösterilir.
    public sealed class AlarmTextProvider
    {
        public const string FileName = "AlarmMetinleri.csv";

        private readonly Dictionary<int, string> _texts =
            new Dictionary<int, string>();

        public string FilePath { get; } =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

        public int LoadedCount => _texts.Count;

        public void Load()
        {
            _texts.Clear();

            try
            {
                if (!File.Exists(FilePath))
                {
                    AppLogger.Warn($"Alarm metin dosyası bulunamadı: {FilePath}. Alarmlar numara ile gösterilecek.");
                    return;
                }

                int lineNo = 0;

                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    lineNo++;
                    string line = raw.Trim();

                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;

                    int sep = line.IndexOf(';');

                    if (sep <= 0 || !int.TryParse(line.Substring(0, sep).Trim(), out int no))
                    {
                        AppLogger.Warn($"{FileName} satır {lineNo} okunamadı: \"{line}\"");
                        continue;
                    }

                    string text = line.Substring(sep + 1).Trim();

                    if (text.Length > 0)
                        _texts[no] = text;
                }

                AppLogger.Info($"{_texts.Count} alarm metni yüklendi ({FilePath}).");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Alarm metin dosyası okunamadı: {FilePath}", ex);
            }
        }

        public string Get(int no)
        {
            return _texts.TryGetValue(no, out string text)
                ? text
                : $"Alarm {no}";
        }
    }
}
