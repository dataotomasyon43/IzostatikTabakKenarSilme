using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// Servis > Tag okuma testi. Uygulama klasöründeki PlcTaglari.csv
    /// (Sysmac değişken dışa aktarımı: "VAR,Ad,Tip,...") okunur ve tüm
    /// değişkenler listelenir; ayrıca CSV'de olmayan birkaç sistem değişkeni
    /// (eksen hata kodları, _MC_ErrSta ...) eklenir. Arama kutusu listeyi
    /// süzer; "Görünenleri oku" yalnız süzülen satırları okur.
    /// Yalnız okur, PLC'ye hiçbir şey yazmaz.
    /// </summary>
    public partial class CanliIzlemePage : Page
    {
        private const string TagFileName = "PlcTaglari.csv";

        // Ana pencereye ait paylaşılan servis; bu sayfa dispose etmez.
        private readonly PlcService _plc;
        private readonly Func<Page> _back;

        private readonly List<TagResult> _tags = new List<TagResult>();
        private readonly ICollectionView _view;

        private CancellationTokenSource _readCts;

        public CanliIzlemePage(PlcService plc, Func<Page> back = null)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _back = back;

            InitializeComponent();

            LoadTags();

            _view = CollectionViewSource.GetDefaultView(_tags);
            _view.Filter = o => Matches((TagResult)o, SearchBox.Text);
            TagGrid.ItemsSource = _view;

            UpdateCounts();
            Unloaded += (s, e) => _readCts?.Cancel();
        }

        // ---------------- Liste ----------------

        private void LoadTags()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TagFileName);
            int fromFile = 0;

            try
            {
                if (File.Exists(path))
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (string line in File.ReadAllLines(path))
                    {
                        string[] f = line.Split(',');

                        if (f.Length < 3 || !string.Equals(f[0].Trim(), "VAR", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string name = f[1].Trim();
                        string type = f[2].Trim();

                        if (name.Length == 0 || type.Length == 0 || !seen.Add(name))
                            continue;

                        _tags.Add(new TagResult { Name = name, DataType = type, Group = "PLC" });
                        fromFile++;
                    }

                    SourceText.Text = $"{TagFileName} · {fromFile} değişken";
                }
                else
                {
                    SourceText.Text = $"{TagFileName} bulunamadı · yalnız ek değişkenler";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Tag listesi okunamadı", ex);
                SourceText.Text = $"{TagFileName} okunamadı: {ex.Message}";
            }

            // CSV'de olmayan sistem değişkenleri: servo / EtherCAT / PLC
            // hatalarının EtherNet/IP'den okunup okunamadığını görmek için.
            // Okunamayanlar "Network Publish" kapalı demektir.
            Extra("AxGTR.MFaultLvl.Active", "BOOL");
            Extra("AxGTR.MFaultLvl.Code", "DWORD");
            Extra("AxGTR.Obsr.Active", "BOOL");
            Extra("AxGTR.Obsr.Code", "DWORD");
            Extra("AxModZim1.MFaultLvl.Active", "BOOL");
            Extra("AxModZim1.MFaultLvl.Code", "DWORD");
            Extra("BomseCpk.MFaultLvl.Code", "DWORD");
            Extra("_MC_ErrSta", "WORD");
            Extra("_EC_ErrSta", "WORD");
            Extra("_PLC_ErrSta", "WORD");
            Extra("_ErrSta", "WORD");
        }

        private void Extra(string name, string type)
        {
            if (_tags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                return;

            _tags.Add(new TagResult { Name = name, DataType = type, Group = "Ek" });
        }

        // Büyük/küçük harf ve Türkçe İ/ı farkı gözetmeden "içerir" araması.
        // Boşlukla ayrılmış birden çok kelimenin hepsi geçmeli.
        private static bool Matches(TagResult tag, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string name = Norm(tag.Name);

            return query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .All(word => name.Contains(Norm(word)));
        }

        private static string Norm(string s)
        {
            return s.Replace('İ', 'I').Replace('ı', 'i').ToUpperInvariant();
        }

        private List<TagResult> VisibleTags()
        {
            return _view.Cast<TagResult>().ToList();
        }

        private void UpdateCounts()
        {
            int visible = _view.Cast<object>().Count();
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            if (_readCts == null)
                StatusText.Text = $"Gösterilen: {visible} / {_tags.Count}. Satıra dokunup SEÇİLİYİ OKU ya da GÖRÜNENLERİ OKU.";
        }

        // ---------------- Arama ----------------

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _view?.Refresh();
            UpdateCounts();
        }

        private void SearchBox_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AskSearch();
            e.Handled = true;
        }

        private void AskSearch()
        {
            string text = IzostatikTabakKenarSilme.Windows.TextInputWindow.Ask(Window.GetWindow(this), "Tag ara", SearchBox.Text,
                "Değişken adının bir kısmı (ör. GTR, Bomse, Alm). Boşlukla birden çok kelime.");

            if (text != null)
                SearchBox.Text = text;
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
        }

        // ---------------- Okuma ----------------

        private void ReadSelected_Click(object sender, RoutedEventArgs e)
        {
            if (TagGrid.SelectedItem is TagResult tag)
                _ = ReadAsync(new List<TagResult> { tag });
            else
                StatusText.Text = "Önce listeden bir satır seçin.";
        }

        private void ReadVisible_Click(object sender, RoutedEventArgs e)
        {
            if (_readCts != null)
            {
                _readCts.Cancel(); // okuma sürüyor: DURDUR
                return;
            }

            _ = ReadAsync(VisibleTags());
        }

        private async Task ReadAsync(List<TagResult> tags)
        {
            if (_readCts != null || tags.Count == 0)
                return;

            var cts = new CancellationTokenSource();
            _readCts = cts;
            ReadVisibleButton.Content = "DURDUR";
            ReadSelectedButton.IsEnabled = false;

            int ok = 0, failed = 0, index = 0;
            var failedNames = new List<string>();

            try
            {
                foreach (TagResult tag in tags)
                {
                    if (cts.IsCancellationRequested)
                        break;

                    index++;
                    StatusText.Text = $"Okunuyor ({index}/{tags.Count}): {tag.Name}";

                    try
                    {
                        string value = await Task.Run(() => ReadTagValue(tag.Name, tag.DataType));

                        if (value == null)
                        {
                            tag.Set("-", "Bu tip okunmuyor", isError: false);
                        }
                        else
                        {
                            tag.Set(value, "OK " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), isError: false);
                            ok++;
                        }
                    }
                    catch (Exception ex)
                    {
                        tag.Set("-", ex.Message, isError: true);
                        failed++;
                        failedNames.Add(tag.Name);
                    }
                }

                string summary = $"{(cts.IsCancellationRequested ? "Durduruldu" : "Bitti")}: {ok} okundu, {failed} hatalı ({index}/{tags.Count}).";
                StatusText.Text = summary;

                if (tags.Count > 1)
                {
                    AppLogger.Info("Tag okuma testi: " + summary +
                        (failedNames.Count > 0 ? " Hatalı: " + string.Join(", ", failedNames.Take(40)) +
                                                 (failedNames.Count > 40 ? " …" : "") : ""));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Tag okuma testinde beklenmeyen hata", ex);
                StatusText.Text = "Beklenmeyen hata: " + ex.Message;
            }
            finally
            {
                _readCts = null;
                ReadVisibleButton.Content = "GÖRÜNENLERİ OKU";
                ReadSelectedButton.IsEnabled = true;
            }
        }

        // Arka plan thread'inde çalışır; yalnız değeri döndürür.
        // Desteklenmeyen tip için null.
        private string ReadTagValue(string name, string type)
        {
            string t = type.ToUpperInvariant();

            // Dizi: "BOOL[0..36]", "REAL[0..59]", "BOOL[501]"
            int br = t.IndexOf('[');
            if (br > 0)
            {
                string baseType = t.Substring(0, br);
                int count = ArrayCount(t.Substring(br));

                if (count <= 0)
                    return null;

                if (baseType == "BOOL")
                {
                    bool[] bits = _plc.ReadAlarmArray(name, count);
                    var on = new List<int>();
                    for (int i = 0; i < bits.Length; i++)
                        if (bits[i]) on.Add(i);

                    return on.Count == 0
                        ? $"{bits.Length} eleman, TRUE yok"
                        : $"{on.Count} TRUE: [{string.Join(",", on.Take(30))}{(on.Count > 30 ? ",…" : "")}]";
                }

                if (baseType == "REAL")
                {
                    float[] v = _plc.ReadRealArray(name, count);
                    return $"{v.Length} eleman: " + string.Join("; ", v.Take(4).Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))) +
                           (v.Length > 4 ? " …" : "");
                }

                return null;
            }

            switch (t)
            {
                case "BOOL": return _plc.ReadBoolTag(name) ? "TRUE" : "FALSE";
                case "INT": return _plc.ReadIntTag(name).ToString(CultureInfo.InvariantCulture);
                case "DINT": return _plc.ReadDIntTag(name).ToString(CultureInfo.InvariantCulture);
                case "REAL": return _plc.ReadRealTag(name).ToString("0.####", CultureInfo.InvariantCulture);
                case "LREAL": return _plc.ReadLRealTag(name).ToString("0.####", CultureInfo.InvariantCulture);
                case "WORD": return $"0x{_plc.ReadWordTag(name):X4}";
                case "UDINT": return _plc.ReadUDintTag(name).ToString(CultureInfo.InvariantCulture);
                case "DWORD": return $"0x{_plc.ReadUDintTag(name):X8}";
                default: return null;
            }
        }

        // "[0..36]" → 37, "[501]" → 501
        private static int ArrayCount(string bracket)
        {
            string inner = bracket.Trim('[', ']');
            int dots = inner.IndexOf("..", StringComparison.Ordinal);

            if (dots >= 0 &&
                int.TryParse(inner.Substring(0, dots), out int lo) &&
                int.TryParse(inner.Substring(dots + 2), out int hi))
            {
                return hi - lo + 1;
            }

            return int.TryParse(inner, out int n) ? n : 0;
        }

        // ---------------- Diğer ----------------

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            _readCts?.Cancel();

            if (_back != null)
                NavigationService?.Navigate(_back());
            else if (NavigationService?.CanGoBack == true)
                NavigationService.GoBack();
        }

        private void TagGrid_ManipulationBoundaryFeedback(object sender, System.Windows.Input.ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }
    }

    public class TagResult : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public string Group { get; set; }

        public string Value { get; private set; } = "-";
        public string Status { get; private set; } = string.Empty;
        public bool IsError { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public void Set(string value, string status, bool isError)
        {
            Value = value;
            Status = status;
            IsError = isError;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsError)));
        }
    }
}
