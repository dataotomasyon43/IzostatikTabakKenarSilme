using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IzostatikTabakKenarSilme.Windows
{
    /// <summary>
    /// Dokunmatik sayı girişi. Ask(...) pencereyi modal açar; TAMAM ile
    /// aralık içindeki değeri, İPTAL / kapatma ile null döndürür.
    /// PLC'ye kendisi yazmaz; yazmayı çağıran yapar.
    /// </summary>
    public partial class NumericKeypadWindow : Window
    {
        private const int MaxLength = 12;

        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private readonly double _min;
        private readonly double _max;
        private readonly int _decimals;
        private readonly string _unit;

        private string _text;

        // İlk tuş mevcut değeri silip yeni girişe başlar.
        private bool _fresh = true;

        public double? Result { get; private set; }

        private NumericKeypadWindow(string title, string unit, double? current,
            double min, double max, int decimals)
        {
            _min = min;
            _max = max;
            _decimals = Math.Max(0, decimals);
            _unit = unit ?? string.Empty;

            InitializeComponent();

            TitleText.Text = title;
            UnitText.Text = _unit;
            DecimalButton.IsEnabled = _decimals > 0;

            CurrentText.Text = current.HasValue
                ? $"Mevcut değer: {Format(current.Value)} {_unit}"
                : "Mevcut değer: —";

            _text = current.HasValue ? Format(current.Value) : string.Empty;
            ShowRange();
            Refresh();

            PreviewKeyDown += Window_PreviewKeyDown;
        }

        /// <summary>
        /// Klavyeyi açar; girilen değeri (ya da iptalde null) döndürür.
        /// </summary>
        public static double? Ask(Window owner, string title, string unit, double? current,
            double min, double max, int decimals)
        {
            var window = new NumericKeypadWindow(title, unit, current, min, max, decimals);

            if (owner != null && owner.IsVisible)
                window.Owner = owner;
            else
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            window.ShowDialog();
            return window.Result;
        }

        private string Format(double value)
        {
            return value.ToString("F" + _decimals, Tr);
        }

        // ---------------- Tuşlar ----------------

        private void Digit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Content is string digit)
                AppendDigit(digit);
        }

        private void AppendDigit(string digit)
        {
            StartFreshIfNeeded();

            // Ondalık hane sınırı
            int comma = _text.IndexOf(',');
            if (comma >= 0 && _text.Length - comma - 1 >= _decimals)
                return;

            if (_text.Length >= MaxLength)
                return;

            // Baştaki gereksiz sıfır: "0" + "5" → "5"
            if (_text == "0")
                _text = digit;
            else
                _text += digit;

            Refresh();
        }

        private void Decimal_Click(object sender, RoutedEventArgs e)
        {
            AppendDecimal();
        }

        private void AppendDecimal()
        {
            if (_decimals == 0)
                return;

            StartFreshIfNeeded();

            if (_text.Contains(","))
                return;

            _text = _text.Length == 0 ? "0," : _text + ",";
            Refresh();
        }

        private void Backspace_Click(object sender, RoutedEventArgs e)
        {
            Backspace();
        }

        private void Backspace()
        {
            _fresh = false;

            if (_text.Length > 0)
                _text = _text.Substring(0, _text.Length - 1);

            Refresh();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _fresh = false;
            _text = string.Empty;
            Refresh();
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
            if (!TryGetValue(out double value))
                return;

            Result = value;
            Close();
        }

        private void StartFreshIfNeeded()
        {
            if (_fresh)
            {
                _fresh = false;
                _text = string.Empty;
            }
        }

        // ---------------- Fiziksel klavye (servis için) ----------------

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key >= Key.D0 && e.Key <= Key.D9)
                AppendDigit(((int)(e.Key - Key.D0)).ToString());
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
                AppendDigit(((int)(e.Key - Key.NumPad0)).ToString());
            else if (e.Key == Key.OemComma || e.Key == Key.OemPeriod || e.Key == Key.Decimal)
                AppendDecimal();
            else if (e.Key == Key.Back)
                Backspace();
            else if (e.Key == Key.Enter)
                Accept();
            else if (e.Key == Key.Escape)
                Close();
            else
                return;

            e.Handled = true;
        }

        // ---------------- Görünüm ----------------

        private void ShowRange()
        {
            HintText.Text = $"Aralık: {Format(_min)} – {Format(_max)} {_unit}".TrimEnd();
            HintText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        }

        private void Refresh()
        {
            ValueText.Text = _text.Length == 0 ? "0" : _text;

            bool ok = TryParse(out double value) && value >= _min && value <= _max;
            OkButton.IsEnabled = ok;

            if (_text.Length > 0 && TryParse(out value) && (value < _min || value > _max))
            {
                HintText.Text = $"Aralık dışında: {Format(_min)} – {Format(_max)} {_unit}".TrimEnd();
                HintText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorColor");
            }
            else
            {
                ShowRange();
            }
        }

        private bool TryParse(out double value)
        {
            string text = _text.Length == 0 ? "0" : _text.TrimEnd(',');
            return double.TryParse(text, NumberStyles.AllowDecimalPoint, Tr, out value);
        }

        private bool TryGetValue(out double value)
        {
            return TryParse(out value) && value >= _min && value <= _max;
        }
    }
}
