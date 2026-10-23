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

        protected override void OnExit(ExitEventArgs e)
        {
            AppLogger.Info($"Uygulama kapandı (çıkış kodu: {e.ApplicationExitCode}).");
            base.OnExit(e);
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
