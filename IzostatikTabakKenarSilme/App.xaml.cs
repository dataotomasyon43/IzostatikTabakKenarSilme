using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme
{
    /// <summary>
    /// App.xaml etkileşim mantığı
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // HMI uygulaması beklenmeyen bir hatada kapanmamalı:
            // UI thread hataları yakalanır, loglanır ve ekranda gösterilir.
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            AppLogger.DeleteOldLogs();
            AppLogger.Info("Uygulama başlatıldı.");

            base.OnStartup(e);
        }

        // Kapanış: ana pencere kapanınca (ShutdownMode=OnMainWindowClose)
        // PLC bağlantısı, buton modülü ve yazma kuyruğu MainWindow.Closed'da
        // zaten kapatılmış olur. Burada:
        //  1. libplctag'in kendi native thread'leri durdurulur
        //     (plc_tag_shutdown). Durdurulmazsa özellikle Windows 7'de süreç
        //     görev yöneticisinde kalabiliyor, plctag.dll kilitli kaldığı için
        //     uygulama yeniden açılınca "dll kullanımda" hatası veriyor.
        //  2. Yine de bir şey süreci tutarsa süreç doğrudan sonlandırılır;
        //     tüm kayıtlar bu noktada zaten diske yazılmıştır.
        protected override void OnExit(ExitEventArgs e)
        {
            AppLogger.Info($"Uygulama kapanıyor (çıkış kodu: {e.ApplicationExitCode}).");

            ShutdownPlcTagLibrary();

            base.OnExit(e);

            AppLogger.Info("Uygulama kapandı.");
            ForceProcessExit(e.ApplicationExitCode);
        }

        // libplctag.NativeImport'taki plctag.plc_tag_shutdown(). Paket
        // sürümüne bağımlı olmamak için yansıma ile çağrılır; yoksa atlanır.
        private static void ShutdownPlcTagLibrary()
        {
            try
            {
                Type native = Type.GetType("libplctag.NativeImport.plctag, libplctag.NativeImport", throwOnError: false);
                System.Reflection.MethodInfo shutdown = native?.GetMethod(
                    "plc_tag_shutdown", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, Type.EmptyTypes, null);

                if (shutdown == null)
                {
                    AppLogger.Warn("libplctag: plc_tag_shutdown bulunamadı, atlandı.");
                    return;
                }

                shutdown.Invoke(null, null);
                AppLogger.Info("libplctag kapatıldı.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("libplctag kapatılamadı", ex);
            }
        }

        private static void ForceProcessExit(int exitCode)
        {
            try
            {
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Süreç sonlandırılamadı", ex);
                Environment.Exit(exitCode);
            }
        }

        // UI thread'inde yakalanmayan hatalar: loglanır, uygulama çalışmaya
        // devam eder ve hata alt durum çubuğunda gösterilir.
        private void App_DispatcherUnhandledException(
            object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            AppLogger.Error("Yakalanmamış arayüz hatası", e.Exception);

            e.Handled = true;

            var window = Current?.MainWindow as global::IzostatikTabakKenarSilme.MainWindow;
            window?.ShowSystemMessage("Hata: " + e.Exception.Message, isError: true);
        }

        // Arka plan thread'lerindeki yakalanmayan hatalar. .NET Framework'te
        // bu durumda sürecin kapanması engellenemez; en azından loglanır.
        // Bu nedenle arka plan işleri kendi içinde try/catch ile korunur.
        private void CurrentDomain_UnhandledException(
            object sender, UnhandledExceptionEventArgs e)
        {
            AppLogger.Error(
                $"Yakalanmamış arka plan hatası (uygulama kapanıyor: {e.IsTerminating})",
                e.ExceptionObject as Exception);
        }

        // await edilmemiş Task'lerdeki hatalar: loglanır ve gözlemlendi
        // olarak işaretlenir.
        private void TaskScheduler_UnobservedTaskException(
            object sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLogger.Error("Gözlemlenmemiş Task hatası", e.Exception);
            e.SetObserved();
        }
    }
}
