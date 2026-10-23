using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using IzostatikTabakKenarSilme.Windows;

namespace IzostatikTabakKenarSilme.Controls
{
    /// <summary>
    /// Ayar satırı: ad, değer kutusu, birim. Kutuya basınca sayı klavyesi
    /// açılır; operatör aralık içinde yeni bir değer onaylarsa
    /// ValueEntered olayı çıkar (PLC'ye yazmayı sayfa yapar).
    ///
    /// Değer PLC'den okunup SetValue ile gösterilir; okunamıyorsa (null)
    /// kutu "—" gösterir ve basılamaz.
    /// </summary>
    public partial class ParameterField : UserControl
    {
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private double? _value;
        private bool _busy;
        private bool _readOnly;

        public ParameterField()
        {
            InitializeComponent();
            UpdateEnabled();
        }

        // Yeni değer onaylandı (eski değerden farklıysa).
        public event EventHandler<double> ValueEntered;

        public string Label
        {
            get => LabelText.Text;
            set => LabelText.Text = value;
        }

        public string Unit
        {
            get => UnitText.Text;
            set => UnitText.Text = value;
        }

        public double Min { get; set; }
        public double Max { get; set; } = 9999;

        // Ondalık hane sayısı (0: tam sayı).
        public int Decimals { get; set; }

        // Değer kutusu ve birim sütunu genişliği (dar kartlar için).
        public double ValueWidth
        {
            get => ValueColumn.Width.Value;
            set => ValueColumn.Width = new GridLength(value);
        }

        public double UnitWidth
        {
            get => UnitColumn.Width.Value;
            set => UnitColumn.Width = new GridLength(value);
        }

        // Değer yazısının boyu (varsayılan 26; dar kutularda küçültülür).
        public double ValueFontSize
        {
            get => ValueText.FontSize;
            set => ValueText.FontSize = value;
        }

        // Yalnız gösterge (ör. aktüel pozisyon): basılamaz, soluk görünmez.
        public bool IsReadOnly
        {
            get => _readOnly;
            set
            {
                _readOnly = value;
                ValueButton.IsHitTestVisible = !value;
                UpdateEnabled();
            }
        }

        public double? Value => _value;

        // PLC'den okunan değeri gösterir (null: bilinmiyor).
        public void SetValue(double? value)
        {
            _value = value;
            ValueText.Text = value.HasValue ? value.Value.ToString("F" + Decimals, Tr) : "—";
            UpdateEnabled();
        }

        // Yazma sürerken kutu basılamaz.
        public void SetBusy(bool busy)
        {
            _busy = busy;
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            ValueButton.IsEnabled = _readOnly || (_value.HasValue && !_busy);
        }

        private void ValueButton_Click(object sender, RoutedEventArgs e)
        {
            if (_readOnly || !_value.HasValue || _busy)
                return;

            double? entered = NumericKeypadWindow.Ask(
                Window.GetWindow(this), Label, Unit, _value, Min, Max, Decimals);

            if (!entered.HasValue)
                return;

            // Aynı değeri tekrar yazma.
            double rounded = Math.Round(entered.Value, Decimals);

            if (_value.HasValue && Math.Round(_value.Value, Decimals) == rounded)
                return;

            ValueEntered?.Invoke(this, rounded);
        }
    }
}
