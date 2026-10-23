using System.Windows.Controls;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// İçeriği henüz yapılmamış sayfalar için yer tutucu.
    /// </summary>
    public partial class PlaceholderPage : Page
    {
        public PlaceholderPage(string title, string subtitle = null)
        {
            InitializeComponent();

            TitleText.Text = title;
            SubtitleText.Text = subtitle ?? string.Empty;
        }
    }
}
