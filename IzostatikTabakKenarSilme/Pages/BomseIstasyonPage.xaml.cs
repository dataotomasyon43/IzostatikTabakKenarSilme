using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using IzostatikTabakKenarSilme.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// Bomse + modül (+ takım) istasyonunun tag'leri ve metinleri.
    /// Takım alanları (ToolHiz, ToolYon, ToolStart) boşsa takım satırı ve
    /// butonu gizlenir.
    /// </summary>
    public sealed class BomseIstasyonConfig
    {
        public string Title;            // "Zımpara-1"
        public string Subtitle;
        public string ImageAsset;       // "/Assets/Zimpara1.png"

        // Bomse
        public string BomseBasPoz;      // REAL, °
        public string BomseAktPoz;      // REAL, °, gösterge
        public string BitisAci;         // REAL, °
        public string GeriCekilmeAci;   // REAL, °
        public string TurSure;          // REAL, s
        public string TurSayi;          // INT, adet
        public string BomseYon;         // BOOL, kalıcı ("geri")
        public string BomseJogP;        // BOOL, basılı tutma
        public string BomseJogN;        // BOOL, basılı tutma

        // Takım (zımpara / sünger motoru)
        public string ToolHizLabel;     // "Taşlama Hızı"
        public string ToolHiz;          // INT, % (0-99)
        public string ToolYon;          // BOOL, kalıcı ("geri")
        public string ToolStartText;    // "ZIMPARA DÖN"
        public string ToolStart;        // BOOL, kalıcı

        // Modül
        public string ModHiz;           // INT, %
        public string ModAktPoz;        // REAL, mm, gösterge
        public string ModBasPoz;        // REAL, mm
        public string Teach;            // BOOL, basılı tutma
        public string ModJogP;          // BOOL, basılı tutma
        public string ModJogN;          // BOOL, basılı tutma
    }

    /// <summary>
    /// AYARLAR > bomse + modül istasyonları (Zımpara, Sünger...) için ortak sayfa.
    ///
    /// Okuma (yalnız sayfa açıkken): parametreler, aktüel pozisyonlar,
    /// kalıcı butonların durumu.
    /// Yazma (yalnız operatör eylemiyle):
    ///   - Parametre kutusunda onaylanan değer → ilgili tag.
    ///   - TEACH, bomse / modül ileri-geri: basılı tuttukça TRUE.
    ///   - GERİ (bomse ve takım yönü), takım çalıştırma: kalıcı.
    /// Ek koşullar PLC tarafında.
    /// </summary>
    public partial class BomseIstasyonPage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        private sealed class Param
        {
            public ParameterField Field;
            public string Tag;
            public bool IsInt;
        }

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;
        private readonly BomseIstasyonConfig _cfg;

        private readonly List<Param> _params = new List<Param>();
        private readonly List<PlcPushButton> _pushes = new List<PlcPushButton>();
        private readonly List<PlcToggleButton> _toggles = new List<PlcToggleButton>();
        private readonly HashSet<ParameterField> _writingFields = new HashSet<ParameterField>();

        private bool? _readOk;

        public BomseIstasyonPage(PlcService plc, PlcCommands commands, MachineState state,
            BomseIstasyonConfig config)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _cfg = config ?? throw new ArgumentNullException(nameof(config));

            InitializeComponent();

            TitleText.Text = _cfg.Title;
            SubtitleText.Text = _cfg.Subtitle ?? string.Empty;
            LoadImage(_cfg.ImageAsset);

            // Parametreler
            AddParam(BomseBasPozField, _cfg.BomseBasPoz, false);
            AddParam(BitisAciField, _cfg.BitisAci, false);
            AddParam(GeriCekilmeAciField, _cfg.GeriCekilmeAci, false);
            AddParam(TurSureField, _cfg.TurSure, false);
            AddParam(TurSayiField, _cfg.TurSayi, true);
            AddParam(ModHizField, _cfg.ModHiz, true);
            AddParam(ModBasPozField, _cfg.ModBasPoz, false);

            // Basılı tutma butonları
            AddPush(TeachButton, _cfg.Teach, "Başlama Poz Teach");
            AddPush(BomseIleriButton, _cfg.BomseJogP, "Bomse İleri Dön");
            AddPush(BomseGeriButton, _cfg.BomseJogN, "Bomse Geri Dön");
            AddPush(ModulIleriButton, _cfg.ModJogP, "Modül İleri");
            AddPush(ModulGeriButton, _cfg.ModJogN, "Modül Geri");

            // Kalıcı butonlar
            AddToggle(BomseYonButton, _cfg.BomseYon, "Bomse Geri Yön");

            // Takım (yoksa gizli)
            bool hasTool = !string.IsNullOrEmpty(_cfg.ToolHiz);

            if (hasTool)
            {
                ToolHizField.Label = _cfg.ToolHizLabel ?? "Takım Hızı";
                AddParam(ToolHizField, _cfg.ToolHiz, true);
                AddToggle(ToolYonButton, _cfg.ToolYon, (_cfg.ToolHizLabel ?? "Takım") + " Geri Yön");
            }
            else
            {
                ToolHizField.Visibility = Visibility.Collapsed;
                ToolYonButton.Visibility = Visibility.Collapsed;
            }

            if (!string.IsNullOrEmpty(_cfg.ToolStart))
            {
                ToolStartButton.Content = _cfg.ToolStartText ?? "TAKIM DÖN";
                AddToggle(ToolStartButton, _cfg.ToolStart, _cfg.ToolStartText ?? "Takım Dön");
            }
            else
            {
                ButtonsPanel.Children.Remove(ToolStartButton);
                ButtonsPanel.Columns = 4;
            }

            PageStatusText.Text = string.IsNullOrEmpty(_cfg.ToolStart)
                ? "Turuncu butonlar basılı tuttukça çalışır. GERİ: bir basışta açılır, bir basışta kapanır."
                : $"Turuncu butonlar basılı tuttukça çalışır. GERİ ve {ToolStartButton.Content}: bir basışta açılır, bir basışta kapanır.";

            ShowUnknown();

            new PagePoller(this, PollInterval, PollAsync, OnReadError);
        }

        private void LoadImage(string asset)
        {
            if (string.IsNullOrEmpty(asset))
                return;

            try
            {
                StationImage.Source = new BitmapImage(new Uri("pack://application:,,," + asset));
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"{_cfg.Title}: görsel yüklenemedi ({asset}): {ex.Message}");
            }
        }

        private void AddParam(ParameterField field, string tag, bool isInt)
        {
            if (string.IsNullOrEmpty(tag))
            {
                field.Visibility = Visibility.Collapsed;
                return;
            }

            var param = new Param { Field = field, Tag = tag, IsInt = isInt };
            _params.Add(param);
            field.ValueEntered += (s, v) => WriteParameter(param, v);
        }

        private void AddPush(PlcPushButton button, string tag, string description)
        {
            button.PlcTag = tag;
            button.Description = $"{_cfg.Title} {description}";
            button.Commands = _commands;
            button.CommandFailed += (s, m) => ShowStatus(m, isError: true);
            _pushes.Add(button);
        }

        private void AddToggle(PlcToggleButton button, string tag, string description)
        {
            button.PlcTag = tag;
            button.Description = $"{_cfg.Title} {description}";
            button.Commands = _commands;
            button.CommandFailed += (s, m) => ShowStatus(m, isError: true);
            _toggles.Add(button);
        }

        // ---------------- Okuma ----------------

        private async Task PollAsync(CancellationToken ct)
        {
            if (!_state.IsConnected)
            {
                ShowUnknown();
                return;
            }

            var paramTags = _params.Select(p => new { p.Tag, p.IsInt }).ToList();
            var toggleTags = _toggles.Select(t => t.PlcTag).ToList();
            string bomseAktTag = _cfg.BomseAktPoz;
            string modAktTag = _cfg.ModAktPoz;

            var d = await Task.Run(() => new
            {
                Params = paramTags
                    .Select(p => p.IsInt ? (double)_plc.ReadIntTag(p.Tag) : _plc.ReadRealTag(p.Tag))
                    .ToList(),
                BomseAkt = string.IsNullOrEmpty(bomseAktTag) ? (double?)null : _plc.ReadRealTag(bomseAktTag),
                ModAkt = string.IsNullOrEmpty(modAktTag) ? (double?)null : _plc.ReadRealTag(modAktTag),
                Toggles = toggleTags.Select(t => _plc.ReadBoolTag(t)).ToList()
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            for (int i = 0; i < _params.Count; i++)
                ShowField(_params[i].Field, d.Params[i]);

            BomseAktPozField.SetValue(d.BomseAkt);
            ModAktPozField.SetValue(d.ModAkt);

            for (int i = 0; i < _toggles.Count; i++)
                _toggles[i].SetState(d.Toggles[i]);

            SetPushEnabled(true);

            if (_readOk == false)
                ShowStatus(string.Empty, isError: false);

            _readOk = true;
        }

        private void OnReadError(Exception ex)
        {
            ShowUnknown();

            if (_readOk != false)
            {
                _readOk = false;
                AppLogger.Warn($"{_cfg.Title} sayfası: okunamadı: {ex.Message}");
                ShowStatus("Değerler okunamadı: " + ex.Message, isError: true);
            }
        }

        private void ShowUnknown()
        {
            foreach (Param p in _params)
                ShowField(p.Field, null);

            BomseAktPozField.SetValue(null);
            ModAktPozField.SetValue(null);

            foreach (PlcToggleButton t in _toggles)
                t.SetState(null);

            // Basılıysa HoldButton "bırakıldı" verir (FALSE yazılır).
            SetPushEnabled(false);
        }

        private void SetPushEnabled(bool enabled)
        {
            foreach (PlcPushButton b in _pushes)
                b.IsEnabled = enabled;
        }

        private void ShowField(ParameterField field, double? value)
        {
            if (!_writingFields.Contains(field))
                field.SetValue(value);
        }

        // ---------------- Parametre yazma ----------------

        private async void WriteParameter(Param param, double value)
        {
            ParameterField field = param.Field;
            string unit = string.IsNullOrEmpty(field.Unit) ? string.Empty : " " + field.Unit;
            string oldText = field.Value.HasValue ? field.Value.Value.ToString("F" + field.Decimals, Tr) : "—";
            string newText = value.ToString("F" + field.Decimals, Tr);
            string description = $"{_cfg.Title}: {field.Label} {oldText} → {newText}{unit}";

            _writingFields.Add(field);
            field.SetBusy(true);

            try
            {
                if (param.IsInt)
                    await _commands.WriteIntAsync(param.Tag, (short)value);
                else
                    await _commands.WriteRealAsync(param.Tag, (float)value);

                AppLogger.Info($"Parametre yazıldı: {description}");
                ShowStatus($"Kaydedildi: {field.Label} = {newText}{unit}", isError: false);
                field.SetValue(value);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Parametre yazılamadı: {description}", ex);
                ShowStatus($"Yazılamadı ({field.Label}): {ex.Message}", isError: true);
            }
            finally
            {
                _writingFields.Remove(field);
                field.SetBusy(false);
            }
        }

        // ---------------- Durum satırı ----------------

        private void ShowStatus(string message, bool isError)
        {
            PageStatusText.Text = string.IsNullOrEmpty(message)
                ? string.Empty
                : $"{DateTime.Now:HH:mm:ss}  {message}";

            PageStatusText.SetResourceReference(
                TextBlock.ForegroundProperty,
                isError ? "ErrorColor" : "SecondaryText");
        }
    }
}
