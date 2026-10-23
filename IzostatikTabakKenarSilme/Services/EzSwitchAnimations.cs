using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IzostatikTabakKenarSilme.Services
{
    // EZ buton modülü lamba animasyonları.
    public static class EzSwitchAnimations
    {
        // Kare aralığı: cihaz ~30 ms aralıklı raporları sorunsuz alıyor.
        private const int FrameMs = 30;

        // Açılış animasyonu:
        //   1) Lambalar 1→5 sırayla, gökkuşağı renkleriyle yanar
        //   2) Gökkuşağı lambalar boyunca akar (soldan sağa dalga)
        // Toplam ~1,8 s. Bitince lambalar son karede kalır; görev renklerini
        // (mod renkleri, 5. tuş durumu) çağıran taraf uygular.
        public static async Task PlayStartupAsync(EzSwitchDevice device, CancellationToken ct)
        {
            const double totalMs = 1800;
            const double stepMs = 90;        // lambaların sırayla yanma aralığı
            const double fadeMs = 120;       // her lambanın yanma süresi
            const double hueSpeed = 540;     // derece / saniye
            const double hueSpread = 60;     // komşu lambalar arası renk farkı

            var clock = Stopwatch.StartNew();

            while (!ct.IsCancellationRequested)
            {
                double t = clock.Elapsed.TotalMilliseconds;

                if (t >= totalMs)
                    break;

                for (int i = 0; i < EzSwitchDevice.LampCount; i++)
                {
                    // 1) sıralı yanma
                    double level = Clamp((t - i * stepMs) / fadeMs, 0, 1);

                    // 2) akan gökkuşağı (lamba 1 önde, renk sağa doğru akar)
                    double hue = t / 1000.0 * hueSpeed - i * hueSpread;

                    HsvToRgb(hue, 1.0, level, out byte r, out byte g, out byte b);
                    device.SetLamp(i + 1, r, g, b, LampMode.Sabit, send: false);
                }

                device.Update();

                try
                {
                    await Task.Delay(FrameMs, ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }

        }

        private static double Clamp(double v, double min, double max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        // HSV (h: derece, s/v: 0..1) → RGB (0..255)
        public static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
        {
            h %= 360;
            if (h < 0) h += 360;

            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;

            double rr, gg, bb;

            if (h < 60) { rr = c; gg = x; bb = 0; }
            else if (h < 120) { rr = x; gg = c; bb = 0; }
            else if (h < 180) { rr = 0; gg = c; bb = x; }
            else if (h < 240) { rr = 0; gg = x; bb = c; }
            else if (h < 300) { rr = x; gg = 0; bb = c; }
            else { rr = c; gg = 0; bb = x; }

            r = (byte)Math.Round((rr + m) * 255);
            g = (byte)Math.Round((gg + m) * 255);
            b = (byte)Math.Round((bb + m) * 255);
        }
    }
}
