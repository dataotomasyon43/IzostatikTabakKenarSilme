using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IzostatikTabakKenarSilme.Services;
using IzostatikTabakKenarSilme.Services.Recete;
using IzostatikTabakKenarSilme.Services.Tabak;
using IzostatikTabakKenarSilme.Windows;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// ÜRETİM > Reçete.
    ///
    /// - YENİ: PLC'deki güncel ayarlar + PLC'ye en son yüklenen tabak formu
    ///   bir adla kaydedilir.
    /// - KAYDET: seçili reçete PLC'deki güncel ayarlarla güncellenir.
    /// - SİL: reçete dosyaları silinir (PLC etkilenmez).
    /// - Seçili reçete ile PLC'deki değerler yan yana; farklı olanlar
    ///   işaretli. Sayfa açıkken PLC değerleri 2 sn'de bir okunur.
    /// - REÇETEYİ YÜKLE: ReceteLoader (ayarlar + tabak formu).
    /// </summary>
    public partial class RecetePage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private readonly MachineState _state;
        private readonly ReceteStore _store = new ReceteStore();
        private readonly TabakLibrary _forms = new TabakLibrary();
        private readonly ReceteLoader _loader;

        private ReceteData _selected;
        private TabakProfile _formProfile;
        private Dictionary<string, double> _plcValues;
        private bool _busy;
        private int _version;

        // Tablo satırlarındaki PLC hücreleri ve fark işaretleri
        private readonly Dictionary<string, TextBlock> _plcCells = new Dictionary<string, TextBlock>();
        private readonly Dictionary<string, Border> _rowMarks = new Dictionary<string, Border>();

        public RecetePage(PlcService plc, PlcCommands commands, MachineState state)
        {
            if (plc == null) throw new ArgumentNullException(nameof(plc));
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            _loader = new ReceteLoader(plc, commands, state);

            InitializeComponent();

            BuildTable();

            Loaded += (s, e) =>
            {
                _state.PropertyChanged += State_PropertyChanged;
                RefreshList(null);
                UpdateAll();
            };

            Unloaded += (s, e) => _state.PropertyChanged -= State_PropertyChanged;

            new PagePoller(this, PollInterval, PollAsync, ex =>
            {
                _plcValues = null;
                UpdateAll();
            });
        }

        private void State_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!_state.IsConnected)
                _plcValues = null;

            UpdateAll();
        }

        private async Task PollAsync(CancellationToken ct)
        {
            if (!_state.IsConnected || _busy)
            {
                if (!_state.IsConnected)
                {
                    _plcValues = null;
                    UpdateAll();
                }
                return;
            }

            Dictionary<string, double> values = await _loader.ReadAllAsync();

            if (ct.IsCancellationRequested)
                return;

            _plcValues = values;
            UpdateAll();
        }

        private void Scroll_ManipulationBoundaryFeedback(object sender, System.Windows.Input.ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }

        // ---------------- Liste ----------------

        private void RefreshList(string selectName)
        {
            List<ReceteData> items;

            try
            {
                items = _store.List();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Reçeteler listelenemedi", ex);
                ShowStatus("Reçete klasörü okunamadı: " + ex.Message, isError: true);
                items = new List<ReceteData>();
            }

            string name = selectName ?? _selected?.Name;
            _selected = null;

            ReceteList.ItemsSource = items;
            ListTitleText.Text = $"REÇETELER ({items.Count})";
            EmptyListText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            ReceteData match = items.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.CurrentCultureIgnoreCase));

            if (match != null)
            {
                ReceteList.SelectedItem = match;
                ReceteList.ScrollIntoView(match);
            }
            else
            {
                Select(null);
            }
        }

        private void ReceteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Select(ReceteList.SelectedItem as ReceteData);
        }

        private async void Select(ReceteData r)
        {
            int version = ++_version;

            _selected = r;
            _lastResultFor = null; // önceki yükleme sonucu yerine yeni durumu göster
            _formProfile = null;
            DrawForm();
            UpdateAll();

            string formPath = r == null ? null : _store.FormPath(r);

            if (formPath == null || !File.Exists(formPath))
                return;

            try
            {
                TabakProfile p = await Task.Run(() => ReceteLoader.ComputeProfile(formPath));

                if (version != _version)
                    return;

                _formProfile = p;
                DrawForm();
                UpdateAll();
            }
            catch (Exception ex)
            {
                if (version == _version)
                    ShowStatus("Reçetenin tabak formu okunamadı: " + ex.Message, isError: true);
            }
        }

        // ---------------- YENİ / KAYDET / SİL ----------------

        private async void NewButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_state.IsConnected)
            {
                ShowStatus("Reçete kaydetmek için PLC bağlantısı gerekli.", isError: true);
                return;
            }

            string name = TextInputWindow.Ask(Window.GetWindow(this), "Yeni reçete adı",
                null, "Makinedeki güncel ayarlar ve PLC'ye son yüklenen tabak formu bu adla kaydedilir.");

            if (name == null)
                return;

            if (_store.Exists(name) &&
                !IzostatikTabakKenarSilme.Windows.ConfirmWindow.Ask(Window.GetWindow(this), "Yeni reçete", $"\"{name}\" adlı reçete var. Üzerine yazılsın mı?",
                    "ÜZERİNE YAZ", "VAZGEÇ", IzostatikTabakKenarSilme.Windows.ConfirmKind.Danger))
            {
                return;
            }

            await SaveFromPlcAsync(new ReceteData { Name = name, Created = ReceteStore.Now() });
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            if (!_state.IsConnected)
            {
                ShowStatus("Reçete kaydetmek için PLC bağlantısı gerekli.", isError: true);
                return;
            }

            if (!IzostatikTabakKenarSilme.Windows.ConfirmWindow.Ask(Window.GetWindow(this), "Reçeteyi kaydet",
                    $"\"{_selected.Name}\" reçetesi makinedeki güncel ayarlarla ve PLC'ye son yüklenen tabak formuyla güncellensin mi?",
                    "KAYDET", "VAZGEÇ"))
            {
                return;
            }

            await SaveFromPlcAsync(_selected);
        }

        // PLC'deki değerleri okuyup reçeteye yazar; tabak formu = PLC'ye
        // son yüklenen form (kütüphanede varsa). Form bulunamazsa mevcut
        // formu korur, hiç yoksa formsuz kaydeder (sorarak).
        private async Task SaveFromPlcAsync(ReceteData r)
        {
            SetBusy(true);

            try
            {
                ShowStatus("PLC'deki ayarlar okunuyor...", isError: false);
                Dictionary<string, double> values = await _loader.ReadAllAsync();

                string formSource = null;
                string formName = r.FormName;

                Tuple<string, DateTime> last = _forms.LoadLastLoaded();
                TabakLibrary.Entry entry = last == null ? null :
                    _forms.List().FirstOrDefault(f => string.Equals(f.Name, last.Item1, StringComparison.CurrentCultureIgnoreCase));

                if (entry != null)
                {
                    formSource = entry.Path;
                    formName = entry.Name;
                }
                else if (r.FormFile == null)
                {
                    if (!IzostatikTabakKenarSilme.Windows.ConfirmWindow.Ask(Window.GetWindow(this), "Reçete kaydet",
                            "PLC'ye son yüklenen tabak formu bulunamadı (Tabak Yükle listesinde yok).\n" +
                            "Reçete tabak formu olmadan kaydedilsin mi? (Yüklenirken yalnız ayarlar yazılır.)",
                            "FORMSUZ KAYDET", "VAZGEÇ", IzostatikTabakKenarSilme.Windows.ConfirmKind.Warning))
                    {
                        ShowStatus("Kaydetme iptal edildi.", isError: false);
                        return;
                    }
                }

                r.Values = values;
                r.FormName = formName;
                r.Modified = ReceteStore.Now();

                _store.Save(r, formSource);

                ShowStatus($"Kaydedildi: \"{r.Name}\" ({values.Count} ayar, form: {r.FormName ?? "yok"}).", isError: false);
                RefreshList(r.Name);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Reçete kaydedilemedi: {r.Name}", ex);
                ShowStatus("Reçete kaydedilemedi: " + ex.Message, isError: true);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            if (!IzostatikTabakKenarSilme.Windows.ConfirmWindow.Ask(Window.GetWindow(this), "Reçete sil",
                    $"\"{_selected.Name}\" reçetesi silinsin mi?\n(PLC'deki ayarlar etkilenmez.)",
                    "SİL", "VAZGEÇ", IzostatikTabakKenarSilme.Windows.ConfirmKind.Danger))
            {
                return;
            }

            try
            {
                string name = _selected.Name;
                _store.Delete(_selected);
                _selected = null;
                RefreshList(null);
                ShowStatus($"Silindi: {name}", isError: false);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Reçete silinemedi", ex);
                ShowStatus("Silinemedi: " + ex.Message, isError: true);
            }
        }

        // ---------------- Yükle ----------------

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || _busy)
                return;

            ReceteData r = _selected;
            string formPath = _store.FormPath(r);
            if (formPath != null && !File.Exists(formPath))
                formPath = null;

            int changes = CountDiff(r);
            string formLine = formPath != null ? $"Tabak formu \"{r.FormName}\" da yüklenecek." : "Reçetede tabak formu yok; yalnız ayarlar yazılacak.";

            if (!IzostatikTabakKenarSilme.Windows.ConfirmWindow.Ask(Window.GetWindow(this), "Reçeteyi yükle",
                    $"\"{r.Name}\" reçetesi PLC'ye yüklenecek.\n\n" +
                    (changes >= 0 ? $"{changes} ayar değişecek. " : "") + formLine + "\n\nDevam edilsin mi?",
                    "PLC'YE YÜKLE", "VAZGEÇ", IzostatikTabakKenarSilme.Windows.ConfirmKind.Warning))
            {
                return;
            }

            SetBusy(true);
            SetVerdict("Yükleniyor...", "WarningColor");

            ReceteLoader.Result result;

            try
            {
                result = await _loader.LoadAsync(r, formPath, new Progress<string>(m => ShowStatus(m, isError: false)));
            }
            catch (Exception ex)
            {
                AppLogger.Error("Reçete yükleme beklenmeyen hata", ex);
                result = new ReceteLoader.Result { Success = false, Message = ex.Message };
            }

            if (result.Success)
            {
                _store.SaveActive(r.Name);

                if (formPath != null && r.FormName != null)
                    RegisterFormAsLoaded(r, formPath);

                ShowStatus("✔ " + result.Message, isError: false);
            }
            else
            {
                ShowStatus("✖ " + result.Message, isError: true);
            }

            SetBusy(false);
            SetVerdict(result.Success ? "✔ Reçete yüklendi" : "✖ Yükleme başarısız", result.Success ? "OkColor" : "ErrorColor");
        }

        // Yüklenen formu Tabak Yükle tarafına da işle: kütüphanede yoksa
        // kopyala, "PLC'de yüklü" bilgisini güncelle.
        private void RegisterFormAsLoaded(ReceteData r, string formPath)
        {
            try
            {
                string target = System.IO.Path.Combine(_forms.Directory, ReceteStore.SafeName(r.FormName) + System.IO.Path.GetExtension(formPath).ToLowerInvariant());

                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(_forms.Directory);
                    File.Copy(formPath, target);
                }

                _forms.SaveLastLoaded(System.IO.Path.GetFileNameWithoutExtension(target), DateTime.Now);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Reçetenin formu kütüphaneye işlenemedi: " + ex.Message);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            ReceteList.IsEnabled = !busy;
            NewButton.IsEnabled = !busy;
            UpdateAll();
        }

        // ---------------- Görünüm ----------------

        // Reçete ile PLC arasındaki fark sayısı (PLC değeri yoksa -1).
        private int CountDiff(ReceteData r)
        {
            if (_plcValues == null || r == null)
                return -1;

            return ReceteParams.All.Count(p =>
                r.Values.TryGetValue(p.Tag, out double rv) &&
                _plcValues.TryGetValue(p.Tag, out double pv) &&
                !p.SameValue(rv, pv));
        }

        private void UpdateAll()
        {
            UpdateActive();
            UpdateTable();
            UpdateButtons();
        }

        private void UpdateActive()
        {
            Tuple<string, string> active = _store.LoadActive();

            if (active == null)
            {
                ActiveText.Text = "—";
                ActiveDiffText.Visibility = Visibility.Collapsed;
                return;
            }

            ActiveText.Text = active.Item1;

            ReceteData r = (ReceteList.ItemsSource as IEnumerable<ReceteData>)?
                .FirstOrDefault(x => string.Equals(x.Name, active.Item1, StringComparison.CurrentCultureIgnoreCase));

            int diff = CountDiff(r);

            if (diff > 0)
            {
                ActiveDiffText.Text = $"{diff} ayar reçeteden farklı";
                ActiveDiffText.Visibility = Visibility.Visible;
            }
            else
            {
                ActiveDiffText.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateButtons()
        {
            SaveButton.IsEnabled = !_busy && _selected != null;
            DeleteButton.IsEnabled = !_busy && _selected != null;

            if (_busy)
            {
                LoadButton.IsEnabled = false;
                return;
            }

            string reason = null;

            if (_selected == null)
                reason = "Reçete seçilmedi";
            else
            {
                List<string> errors = ReceteLoader.Validate(_selected);

                if (errors.Count > 0)
                    reason = $"Reçetede {errors.Count} geçersiz / eksik ayar";
                else if (_selected.FormFile != null && _formProfile == null)
                    reason = "Tabak formu okunuyor / okunamadı";
                else if (_formProfile != null && !_formProfile.IsValid)
                    reason = "Reçetenin tabak formu geçersiz";
                else if (_formProfile != null && ReceteLoader.CheckModules(_selected, _formProfile).Any(m => !m.Ok))
                    reason = "Mekanik sınır aşılıyor (400-600 mm)";
                else
                    reason = _loader.CheckPreconditions();
            }

            LoadButton.IsEnabled = reason == null;

            if (VerdictText.Text.StartsWith("✔ Reçete yüklendi") || VerdictText.Text.StartsWith("✖ Yükleme başarısız"))
            {
                // Yükleme sonucunu, seçim değişene kadar göster.
                if (_selected != null && _lastResultFor == _selected.Name)
                    return;
            }

            if (reason == null)
                SetVerdict("✔ Yüklenebilir", "OkColor");
            else
                SetVerdict(reason, _selected == null ? "SecondaryText" : "WarningColor");
        }

        private string _lastResultFor;

        private void SetVerdict(string text, string colorKey)
        {
            if (text.StartsWith("✔ Reçete yüklendi") || text.StartsWith("✖ Yükleme başarısız"))
                _lastResultFor = _selected?.Name;

            VerdictText.Text = text;
            VerdictText.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
            VerdictBorder.SetResourceReference(Border.BorderBrushProperty, colorKey == "SecondaryText" ? "PanelBorder" : colorKey);
        }

        // Tablo: gruplar + satırlar bir kez kurulur; değerler güncellenir.
        private void BuildTable()
        {
            string group = null;

            foreach (ReceteParam p in ReceteParams.All)
            {
                if (p.Group != group)
                {
                    group = p.Group;

                    var header = new TextBlock
                    {
                        Text = group.ToUpperInvariant(),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, TablePanel.Children.Count == 0 ? 0 : 10, 0, 3)
                    };
                    header.SetResourceReference(TextBlock.ForegroundProperty, "AccentColor");
                    TablePanel.Children.Add(header);
                }

                var row = new Grid { Margin = new Thickness(0, 0, 0, 1), MinHeight = 26 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

                // Fark işareti (sol şerit)
                var mark = new Border { Width = 3, Margin = new Thickness(0, 3, 3, 3), Visibility = Visibility.Hidden };
                mark.SetResourceReference(Border.BackgroundProperty, "WarningColor");
                row.Children.Add(mark);

                var label = new TextBlock { Text = p.Label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);

                var rcp = new TextBlock
                {
                    FontSize = 14, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                    Tag = p.Tag
                };
                Grid.SetColumn(rcp, 2);
                row.Children.Add(rcp);

                var plc = new TextBlock
                {
                    FontSize = 14, FontFamily = new FontFamily("Consolas"),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
                };
                plc.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                Grid.SetColumn(plc, 3);
                row.Children.Add(plc);

                row.Tag = rcp;
                _plcCells[p.Tag] = plc;
                _rowMarks[p.Tag] = mark;
                _recipeCells[p.Tag] = rcp;

                TablePanel.Children.Add(row);
            }
        }

        private readonly Dictionary<string, TextBlock> _recipeCells = new Dictionary<string, TextBlock>();

        private void UpdateTable()
        {
            ReceteData r = _selected;

            ReceteNameText.Text = r?.Name ?? "Reçete seçilmedi";
            FormInfoText.Text = r == null ? string.Empty
                : r.FormName == null ? "Tabak formu: yok (yalnız ayarlar)"
                : _formProfile != null && _formProfile.IsValid
                    ? $"Tabak formu: {r.FormName}  ·  {_formProfile.Bounds.Width:0.#} × {_formProfile.Bounds.Height:0.#} mm  ·  kurs {_formProfile.RMax - _formProfile.RMin:0.0} mm"
                    : $"Tabak formu: {r.FormName}";

            int diffCount = 0;

            foreach (ReceteParam p in ReceteParams.All)
            {
                double? rv = r != null && r.Values.TryGetValue(p.Tag, out double v1) ? v1 : (double?)null;
                double? pv = _plcValues != null && _plcValues.TryGetValue(p.Tag, out double v2) ? v2 : (double?)null;

                TextBlock rc = _recipeCells[p.Tag];
                rc.Text = r == null ? string.Empty : p.Format(rv);
                rc.SetResourceReference(TextBlock.ForegroundProperty,
                    rv.HasValue && !p.InRange(rv.Value) ? "ErrorColor" : "PrimaryText");

                _plcCells[p.Tag].Text = p.Format(pv);

                bool diff = rv.HasValue && pv.HasValue && !p.SameValue(rv.Value, pv.Value);
                _rowMarks[p.Tag].Visibility = diff ? Visibility.Visible : Visibility.Hidden;

                if (diff)
                    diffCount++;
            }

            if (r == null)
                DiffText.Text = string.Empty;
            else if (_plcValues == null)
                DiffText.Text = _state.IsConnected ? "PLC değerleri okunuyor..." : "PLC bağlı değil: karşılaştırma yok";
            else
                DiffText.Text = diffCount == 0 ? "PLC'deki ayarlar reçeteyle aynı" : $"{diffCount} ayar PLC'dekinden farklı (turuncu işaretli)";

            DiffText.SetResourceReference(TextBlock.ForegroundProperty,
                r != null && _plcValues != null && diffCount > 0 ? "WarningColor" : "SecondaryText");
        }

        // Küçük form önizlemesi
        private void DrawForm()
        {
            Canvas c = FormCanvas;
            c.Children.Clear();

            TabakProfile p = _formProfile;
            if (p == null || p.Contour.Count < 3)
                return;

            double w = 84, h = 68; // Border 96x80 - kenar boşlukları
            Rect b = p.Bounds;
            double s = Math.Min(w / Math.Max(b.Width, 1e-6), h / Math.Max(b.Height, 1e-6));
            Point ctr = p.Center;

            var poly = new Polygon
            {
                Points = new PointCollection(p.Contour.Select(q => new Point(w / 2 + (q.X - ctr.X) * s, h / 2 - (q.Y - ctr.Y) * s))),
                StrokeThickness = 1.5
            };
            poly.SetResourceReference(Shape.StrokeProperty, "AccentColor");
            c.Children.Add(poly);
        }

        private void ShowStatus(string message, bool isError)
        {
            PageStatusText.Text = string.IsNullOrEmpty(message) ? string.Empty : $"{DateTime.Now:HH:mm:ss}  {message}";
            PageStatusText.SetResourceReference(TextBlock.ForegroundProperty, isError ? "ErrorColor" : "SecondaryText");
        }
    }
}
