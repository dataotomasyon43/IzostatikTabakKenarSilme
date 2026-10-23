using System;
using System.Windows;
using System.Windows.Controls;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Controls
{
    // PLC'ye bağlı kalıcı (aç/kapa) buton: her basış PLC'den son okunan
    // değerin tersini PlcTag'e yazar. TRUE iken yeşil ve vurgulu (Tag =
    // "Active", CommandButtonStyle), FALSE iken tema varsayılanı; durum
    // bilinmiyorsa (SetState(null)) basılamaz.
    //
    // Durumu sayfa okur ve SetState ile verir; yazma yalnızca operatör
    // basınca olur. Hata olursa CommandFailed olayı çıkar.
    public class PlcToggleButton : Button
    {
        private bool? _state;
        private bool _busy;

        public PlcToggleButton()
        {
            Focusable = false;
            ApplyVisual();
        }

        public string PlcTag { get; set; }
        public string Description { get; set; }
        public PlcCommands Commands { get; set; }

        public bool? State => _state;

        public event EventHandler<string> CommandFailed;

        // PLC'den okunan durum (null: bilinmiyor). Yazma sürerken yok sayılır.
        public void SetState(bool? on)
        {
            if (_busy)
                return;

            _state = on;
            ApplyVisual();
        }

        protected override async void OnClick()
        {
            base.OnClick();

            if (Commands == null || string.IsNullOrEmpty(PlcTag) || !_state.HasValue || _busy)
                return;

            bool current = _state.Value;
            bool target = !current;
            string text = $"{Description ?? PlcTag} = {(target ? "AÇIK" : "KAPALI")}";

            _busy = true;
            ApplyVisual();

            try
            {
                await Commands.SetBoolAsync(PlcTag, target);
                AppLogger.Info($"Manuel komut: {text}");
                _state = target;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Manuel komut gönderilemedi: {text}", ex);
                CommandFailed?.Invoke(this, $"Komut gönderilemedi ({text}): {ex.Message}");
            }
            finally
            {
                _busy = false;
                ApplyVisual();
            }
        }

        private void ApplyVisual()
        {
            if (_state == true)
            {
                SetResourceReference(ForegroundProperty, "OkColor");
                Tag = "Active";
            }
            else
            {
                SetResourceReference(ForegroundProperty, "PrimaryText");
                Tag = null;
            }

            IsEnabled = _state.HasValue && !_busy;
        }
    }
}
