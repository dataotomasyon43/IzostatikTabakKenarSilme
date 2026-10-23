using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IzostatikTabakKenarSilme.Controls
{
    // Basılı tutma (push button) butonu: basılınca Pressed, bırakılınca
    // (veya parmak butondan kayınca / yakalama kaybolunca / buton devre dışı
    // kalınca) Released olayı verir. Start, Stop, sıfırla, jog gibi PLC'de
    // "basılıyken TRUE" çalışan komutlar içindir.
    //
    // WPF Button'un IsPressed durumunu kullanır; dokunmatikte "basılı tut =
    // sağ tık" gecikmesi kapatılır. Her Pressed'in ardından mutlaka bir
    // Released gelir.
    public class HoldButton : Button
    {
        public event EventHandler Pressed;
        public event EventHandler Released;

        // Şu anda basılı sayılıyor mu (Pressed verildi, Released verilmedi).
        public bool IsHeld { get; private set; }

        public HoldButton()
        {
            Stylus.SetIsPressAndHoldEnabled(this, false);
            Stylus.SetIsFlicksEnabled(this, false);
            Focusable = false;

            IsEnabledChanged += (s, e) =>
            {
                if (!IsEnabled)
                    EndHold();
            };

            Unloaded += (s, e) => EndHold();
        }

        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);

            if (IsPressed)
                BeginHold();
            else
                EndHold();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            EndHold();
        }

        private void BeginHold()
        {
            if (IsHeld)
                return;

            IsHeld = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        private void EndHold()
        {
            if (!IsHeld)
                return;

            IsHeld = false;
            Released?.Invoke(this, EventArgs.Empty);
        }
    }
}
