using System;
using System.Windows;
using System.Windows.Controls;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// Ayarlar: sistem, bakım ve servis araçları.
    /// Canlı İzleme buradan açılır (ana pencere sayfayı değiştirir).
    /// </summary>
    public partial class AyarlarPage : Page
    {
        private readonly Action _openLiveMonitor;

        public AyarlarPage(Action openLiveMonitor)
        {
            _openLiveMonitor = openLiveMonitor;

            InitializeComponent();
        }

        private void LiveMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            _openLiveMonitor?.Invoke();
        }
    }
}
