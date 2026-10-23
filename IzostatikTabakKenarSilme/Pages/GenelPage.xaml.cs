using System;
using System.Windows;
using System.Windows.Controls;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// Genel: gelişmiş ayarlar ve servis araçları (Canlı İzleme).
    /// Alt sayfaları ana pencere açar (menüde GENEL seçili kalır).
    /// </summary>
    public partial class GenelPage : Page
    {
        private readonly Action _openAdvancedSettings;
        private readonly Action _openLiveMonitor;

        public GenelPage(Action openAdvancedSettings, Action openLiveMonitor)
        {
            _openAdvancedSettings = openAdvancedSettings;
            _openLiveMonitor = openLiveMonitor;

            InitializeComponent();
        }

        private void AdvancedSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _openAdvancedSettings?.Invoke();
        }

        private void LiveMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            _openLiveMonitor?.Invoke();
        }
    }
}
