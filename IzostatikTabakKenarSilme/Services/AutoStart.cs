using System;
using System.Linq;
using Microsoft.Win32;

namespace IzostatikTabakKenarSilme.Services
{
    /// <summary>
    /// Çalışma ortamı: uygulama bir "bin" klasörünün içinden çalışıyorsa
    /// (Visual Studio: bin\Debug, bin\Release) geliştirme sayılır.
    /// Başka bir klasöre kopyalanmışsa (HMI PC'si) saha kurulumu sayılır:
    /// tam ekran açılır ve Windows açılışına eklenir.
    /// </summary>
    public static class AppEnvironment
    {
        public static bool IsDevelopment { get; } = DetectBinFolder();

        private static bool DetectBinFolder()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            string[] parts = dir.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Any(p => string.Equals(p, "bin", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Windows açılışında otomatik başlatma: geçerli kullanıcının
    /// HKCU\...\CurrentVersion\Run anahtarına exe'nin tam yolu yazılır.
    /// Yönetici izni gerektirmez, Windows 7-11'de aynı çalışır. Kullanıcı
    /// oturumu açılınca (otomatik oturum açma ayarlı olmalı) uygulama başlar.
    /// Exe başka klasöre taşınırsa bir sonraki açılışta yol güncellenir.
    /// </summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "IzostatikTabakKenarSilme";

        public static void EnsureRegistered()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrEmpty(exe))
                    return;

                string command = "\"" + exe + "\"";

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null)
                    {
                        AppLogger.Warn("Otomatik başlatma: Run anahtarı açılamadı.");
                        return;
                    }

                    string current = key.GetValue(ValueName) as string;

                    if (string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                        return;

                    key.SetValue(ValueName, command, RegistryValueKind.String);
                    AppLogger.Info($"Otomatik başlatma kaydedildi: {command}" +
                        (current != null ? $" (önceki: {current})" : ""));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Otomatik başlatma kaydedilemedi", ex);
            }
        }
    }
}
