using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Shapes;
using IzostatikTabakKenarSilme.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Pages
{
    /// <summary>
    /// AYARLAR > Transfer Yükleme (GTR).
    ///
    /// Okuma (yalnız sayfa açıkken, PagePoller): parametreler, aktüel
    /// pozisyon, piston sensörleri, kalıcı butonların durumu.
    /// Yazma (yalnız operatör eylemiyle):
    ///   - Parametre kutusunda onaylanan değer → ilgili tag.
    ///   - Turuncu butonlar (öğren, poz git, konveyöre, göbeğe): basılı
    ///     tuttukça TRUE, bırakınca FALSE.
    ///   - Piston aşağı / vakum / üfleme: kalıcı (bir basış TRUE, bir
    ///     basış FALSE).
    /// Ek koşullar (mod, kilitlemeler) PLC tarafında.
    /// </summary>
    public partial class TransferYuklemePage : Page
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        // Ayar satırı ↔ tag (INT ya da REAL).
        private sealed class Param
        {
            public ParameterField Field;
            public string Tag;
            public bool IsInt;
        }

        private readonly PlcService _plc;
        private readonly PlcCommands _commands;
        private readonly MachineState _state;

        private readonly List<Param> _params;
        private readonly List<PlcToggleButton> _toggles;
        private readonly List<PlcPushButton> _pushes;

        // Yazması süren parametreler (okuma bunların değerini ezmez).
        private readonly HashSet<ParameterField> _writingFields = new HashSet<ParameterField>();

        private bool? _readOk;

        public TransferYuklemePage(PlcService plc, PlcCommands commands, MachineState state)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent();

            _params = new List<Param>
            {
                new Param { Field = HizBosField, Tag = PlcTags.GtrHizBos, IsInt = true },
                new Param { Field = IvmeBosField, Tag = PlcTags.GtrIvmeBos, IsInt = true },
                new Param { Field = HizDoluField, Tag = PlcTags.GtrHizDolu, IsInt = true },
                new Param { Field = IvmeDoluField, Tag = PlcTags.GtrIvmeDolu, IsInt = true },
                new Param { Field = AlmaPozField, Tag = PlcTags.GtrAlmaPoz },
                new Param { Field = BirakmaPozField, Tag = PlcTags.GtrBirakmaPoz },
                new Param { Field = VakumSureField, Tag = PlcTags.GtrVakumSure },
                new Param { Field = UflemeSureField, Tag = PlcTags.GtrUflemeSure },
                new Param { Field = PrestenAlmaSureField, Tag = PlcTags.GtrPrestenAlmaSure }
            };

            foreach (Param p in _params)
            {
                Param param = p;
                param.Field.ValueEntered += (s, v) => WriteParameter(param, v);
            }

            // Basılı tutma butonları
            Bind(AlmaOgrenButton, PlcTags.GtrAlmaPozOgren);
            Bind(BirakmaOgrenButton, PlcTags.GtrBirakmaPozOgren);
            Bind(AlmayaGitButton, PlcTags.GtrAlmayaGit);
            Bind(BirakmayaGitButton, PlcTags.GtrBirakmayaGit);
            Bind(KonveyoreButton, PlcTags.GtrKonveyore);
            Bind(GobegeButton, PlcTags.GtrGobege);

            _pushes = new List<PlcPushButton>
            {
                AlmaOgrenButton, BirakmaOgrenButton, AlmayaGitButton,
                BirakmayaGitButton, KonveyoreButton, GobegeButton
            };

            // Kalıcı butonlar
            Bind(Piston1AsagiButton, PlcTags.GtrPiston1Asagi);
            Bind(Piston2AsagiButton, PlcTags.GtrPiston2Asagi);
            Bind(Piston1VakumButton, PlcTags.GtrPiston1Vakum);
            Bind(Piston2VakumButton, PlcTags.GtrPiston2Vakum);
            Bind(Piston1UflemeButton, PlcTags.GtrPiston1Ufleme);
            Bind(Piston2UflemeButton, PlcTags.GtrPiston2Ufleme);

            _toggles = new List<PlcToggleButton>
            {
                Piston1AsagiButton, Piston2AsagiButton, Piston1VakumButton,
                Piston2VakumButton, Piston1UflemeButton, Piston2UflemeButton
            };

            ShowUnknown();

            new PagePoller(this, PollInterval, PollAsync, OnReadError);
        }

        private void Bind(PlcPushButton button, string tag)
        {
            button.PlcTag = tag;
            button.Commands = _commands;
            button.CommandFailed += (s, message) => ShowStatus(message, isError: true);
        }

        private void Bind(PlcToggleButton button, string tag)
        {
            button.PlcTag = tag;
            button.Commands = _commands;
            button.CommandFailed += (s, message) => ShowStatus(message, isError: true);
        }

        // ---------------- Okuma ----------------

        private async Task PollAsync(CancellationToken ct)
        {
            // Bağlantı yokken deneme; ana pencere bağlantıyı izliyor.
            if (!_state.IsConnected)
            {
                ShowUnknown();
                return;
            }

            // Okunacak tag adları (UI thread'inde toplanır).
            var paramTags = _params.Select(p => new { p.Tag, p.IsInt }).ToList();
            var toggleTags = _toggles.Select(t => t.PlcTag).ToList();

            var d = await Task.Run(() => new
            {
                Params = paramTags
                    .Select(p => p.IsInt ? (double)_plc.ReadIntTag(p.Tag) : _plc.ReadRealTag(p.Tag))
                    .ToList(),
                AktPoz = _plc.ReadRealTag(PlcTags.GtrAktPoz),
                P1Yukarida = _plc.ReadBoolTag(PlcTags.GtrPiston1Yukarida),
                P1Baski = _plc.ReadBoolTag(PlcTags.GtrPiston1Baski),
                P2Yukarida = _plc.ReadBoolTag(PlcTags.GtrPiston2Yukarida),
                P2Baski = _plc.ReadBoolTag(PlcTags.GtrPiston2Baski),
                Toggles = toggleTags.Select(t => _plc.ReadBoolTag(t)).ToList()
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            for (int i = 0; i < _params.Count; i++)
                ShowField(_params[i].Field, d.Params[i]);

            AktPozField.SetValue(d.AktPoz);

            SetLamp(Piston1YukaridaLamp, d.P1Yukarida);
            SetLamp(Piston1BaskiLamp, d.P1Baski);
            SetLamp(Piston2YukaridaLamp, d.P2Yukarida);
            SetLamp(Piston2BaskiLamp, d.P2Baski);

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
                AppLogger.Warn($"Transfer Yükleme sayfası: okunamadı: {ex.Message}");
                ShowStatus("Değerler okunamadı: " + ex.Message, isError: true);
            }
        }

        private void ShowUnknown()
        {
            foreach (Param p in _params)
                ShowField(p.Field, null);

            AktPozField.SetValue(null);

            SetLamp(Piston1YukaridaLamp, null);
            SetLamp(Piston1BaskiLamp, null);
            SetLamp(Piston2YukaridaLamp, null);
            SetLamp(Piston2BaskiLamp, null);

            foreach (PlcToggleButton t in _toggles)
                t.SetState(null);

            // Bağlantı yokken basılı tutma butonları devre dışı (basılıysa
            // HoldButton "bırakıldı" verir).
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

        private static void SetLamp(Ellipse lamp, bool? on)
        {
            lamp.SetResourceReference(Shape.FillProperty, on == true ? "OkColor" : "PanelBorder");
            lamp.Opacity = on.HasValue ? 1.0 : 0.4;
        }

        // ---------------- Parametre yazma ----------------

        private async void WriteParameter(Param param, double value)
        {
            ParameterField field = param.Field;
            string unit = string.IsNullOrEmpty(field.Unit) ? string.Empty : " " + field.Unit;
            string oldText = field.Value.HasValue ? field.Value.Value.ToString("F" + field.Decimals, Tr) : "—";
            string newText = value.ToString("F" + field.Decimals, Tr);
            string description = $"Transfer Yükleme: {field.Label} {oldText} → {newText}{unit}";

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

                // Bir sonraki okuma PLC'den doğrular.
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
