using System;
using System.IO;
using System.Text;

namespace IzostatikTabakKenarSilme.Services
{
    // Basit, thread güvenli dosya loglayıcı.
    // Günlük dosya: %LOCALAPPDATA%\IzostatikTabakKenarSilme\Logs\yyyy-MM-dd.log
    // Log yazılamazsa (disk dolu, izin yok vb.) uygulama ASLA durmaz;
    // hata sessizce yutulur.
    public static class AppLogger
    {
        private static readonly object _sync = new object();

        private const int KeepDays = 30;

        public static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IzostatikTabakKenarSilme",
            "Logs");

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", message, ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                DateTime now = DateTime.Now;

                var sb = new StringBuilder();
                sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(" [").Append(level).Append("] ")
                  .Append(message);

                if (ex != null)
                    sb.AppendLine().Append(ex);

                sb.AppendLine();

                string file = Path.Combine(LogDirectory, now.ToString("yyyy-MM-dd") + ".log");

                lock (_sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Log yazılamaması uygulamayı durdurmamalı.
            }
        }

        // KeepDays günden eski log dosyalarını siler.
        public static void DeleteOldLogs()
        {
            try
            {
                lock (_sync)
                {
                    if (!Directory.Exists(LogDirectory))
                        return;

                    DateTime limit = DateTime.Now.AddDays(-KeepDays);

                    foreach (string file in Directory.GetFiles(LogDirectory, "*.log"))
                    {
                        if (File.GetLastWriteTime(file) < limit)
                            File.Delete(file);
                    }
                }
            }
            catch
            {
                // Temizlik başarısız olursa önemli değil.
            }
        }
    }
}
