
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme
{
    public partial class MainWindow : Window
    {
        private bool _isDarkTheme = true;

        private readonly PlcService _plcService = new PlcService();

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
        }
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _plcService.Dispose();
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            string themePath = _isDarkTheme
                ? "Themes/LightTheme.xaml"
                : "Themes/DarkTheme.xaml";

            var dictionaries =
                Application.Current.Resources.MergedDictionaries;

            dictionaries.Clear();

            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(themePath, UriKind.Relative)
            });

            _isDarkTheme = !_isDarkTheme;

            ThemeButton.Content = _isDarkTheme
                ? "☀"
                : "  ☾ ";
        }

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;

            if (button == null)
                return;

            switch (button.Tag as string)
            {
                case "Home":
                    MainFrame.Navigate(new Pages.AnaEkranPage());
                    break;

                case "TabakYukle":
                    MainFrame.Navigate(new Pages.TabakYuklePage());
                    break;

                case "CanliIzleme":
                    MainFrame.Navigate(new Pages.CanliIzlemePage());
                    break;

                case "Receteler":
                    MainFrame.Navigate(new Pages.RecetelerPage());
                    break;

                case "Ayarlar":
                    MainFrame.Navigate(new Pages.AyarlarPage());
                    break;

                case "AlarmGecmisi":
                    MainFrame.Navigate(new Pages.AlarmGecmisiPage());
                    break;
            }
        }



    }
}
