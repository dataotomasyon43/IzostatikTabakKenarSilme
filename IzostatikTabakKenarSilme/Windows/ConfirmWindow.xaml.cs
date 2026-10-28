using System.Windows;
using System.Windows.Input;

namespace IzostatikTabakKenarSilme.Windows
{
    public enum ConfirmKind
    {
        Question,   // vurgu rengi, "?"
        Warning,    // turuncu, "!" (PLC'ye yazma gibi)
        Danger      // kırmızı, "!" (silme / üzerine yazma)
    }

    /// <summary>
    /// Dokunmatiğe uygun onay penceresi; Windows MessageBox yerine.
    /// Ask(...) EVET'e basılırsa true, HAYIR / ESC / pencere kapanırsa false.
    /// </summary>
    public partial class ConfirmWindow : Window
    {
        private bool _result;

        private ConfirmWindow(string title, string message, string yesText, string noText, ConfirmKind kind)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;
            YesButton.Content = yesText;
            NoButton.Content = noText;

            string color = kind == ConfirmKind.Danger ? "ErrorColor"
                : kind == ConfirmKind.Warning ? "WarningColor"
                : "AccentColor";

            Frame.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, color);
            IconBadge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, color);
            IconText.Text = kind == ConfirmKind.Question ? "?" : "!";

            if (kind != ConfirmKind.Question)
                YesButton.SetResourceReference(ForegroundProperty, color);

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    _result = true;
                    Close();
                    e.Handled = true;
                }
            };
        }

        public static bool Ask(Window owner, string title, string message,
            string yesText = "EVET", string noText = "HAYIR", ConfirmKind kind = ConfirmKind.Question)
        {
            var w = new ConfirmWindow(title, message, yesText, noText, kind);

            if (owner != null && owner.IsVisible)
                w.Owner = owner;
            else
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            w.ShowDialog();
            return w._result;
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            _result = true;
            Close();
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            _result = false;
            Close();
        }
    }
}
