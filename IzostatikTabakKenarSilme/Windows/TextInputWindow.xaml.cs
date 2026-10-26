using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IzostatikTabakKenarSilme.Windows
{
    /// <summary>
    /// Dokunmatik metin girişi (ör. reçete adı). Ask(...) modal açar; TAMAM ile
    /// metni (baştaki/sondaki boşluklar kırpılmış), İPTAL ile null döndürür.
    /// </summary>
    public partial class TextInputWindow : Window
    {
        private static readonly string[] Rows =
        {
            "1234567890-",
            "QWERTYUIOPĞÜ",
            "ASDFGHJKLŞİ",
            "ZXCVBNMÖÇ.",
        };

        public string Result { get; private set; }

        private TextInputWindow(string title, string initial, string hint)
        {
            InitializeComponent();

            TitleText.Text = title;
            HintText.Text = hint ?? string.Empty;
            InputBox.Text = initial ?? string.Empty;
            InputBox.CaretIndex = InputBox.Text.Length;

            BuildKeys();
            UpdateOk();

            Loaded += (s, e) => InputBox.Focus();
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        public static string Ask(Window owner, string title, string initial = null, string hint = null)
        {
            var w = new TextInputWindow(title, initial, hint);

            if (owner != null && owner.IsVisible)
                w.Owner = owner;
            else
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            w.ShowDialog();
            return w.Result;
        }

        private void BuildKeys()
        {
            var style = (Style)FindResource("KeyStyle");

            foreach (string row in Rows)
            {
                var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = row.Length };

                foreach (char ch in row)
                {
                    var b = new Button { Content = ch.ToString(), Style = style };
                    b.Click += (s, e) => Insert(((Button)s).Content.ToString());
                    grid.Children.Add(b);
                }

                KeysPanel.Children.Add(grid);
            }

            // Son satır: boşluk + sil
            var last = new Grid();
            last.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            last.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var space = new Button { Content = "BOŞLUK", Style = style };
            space.Click += (s, e) => Insert(" ");
            last.Children.Add(space);

            var back = new Button { Content = "⌫", Style = style };
            back.Click += (s, e) => Backspace();
            Grid.SetColumn(back, 1);
            last.Children.Add(back);

            KeysPanel.Children.Add(last);
        }

        private void Insert(string text)
        {
            int pos = InputBox.SelectionStart;
            string cur = InputBox.Text.Remove(pos, InputBox.SelectionLength);

            if (cur.Length + text.Length > InputBox.MaxLength)
                return;

            InputBox.Text = cur.Insert(pos, text);
            InputBox.CaretIndex = pos + text.Length;
            InputBox.Focus();
        }

        private void Backspace()
        {
            int pos = InputBox.SelectionStart;

            if (InputBox.SelectionLength > 0)
            {
                InputBox.Text = InputBox.Text.Remove(pos, InputBox.SelectionLength);
                InputBox.CaretIndex = pos;
            }
            else if (pos > 0)
            {
                InputBox.Text = InputBox.Text.Remove(pos - 1, 1);
                InputBox.CaretIndex = pos - 1;
            }

            InputBox.Focus();
        }

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateOk();
        }

        private void UpdateOk()
        {
            OkButton.IsEnabled = InputBox.Text.Trim().Length > 0;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            InputBox.Clear();
            InputBox.Focus();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            Close();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Accept();
        }

        private void Accept()
        {
            string text = InputBox.Text.Trim();

            if (text.Length == 0)
                return;

            Result = text;
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
