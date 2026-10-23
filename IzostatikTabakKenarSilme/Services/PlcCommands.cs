using System;
using System.Threading;
using System.Threading.Tasks;

namespace IzostatikTabakKenarSilme.Services
{
    // Operatör komutları (PLC'ye yazma). Hepsi PlcWriteQueue üzerinden,
    // geliş sırasıyla gider; böylece "bas → TRUE" ve "bırak → FALSE"
    // yazmalarının sırası hiçbir zaman karışmaz.
    //
    // Bu sınıf yalnızca operatörün açık bir eylemiyle (ekran butonu,
    // ışıklı buton) çağrılır; kendiliğinden hiçbir şey yazmaz.
    // Dönen Task'ler hata durumunda istisna fırlatır; çağıran yakalar.
    public sealed class PlcCommands
    {
        private const int ReleaseRetryCount = 3;

        private readonly PlcService _plc;
        private readonly PlcWriteQueue _queue;

        public PlcCommands(PlcService plc, PlcWriteQueue queue)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        // Mode (INT) yazar: 0 Kapalı, 1 Güvenli, 2 Manuel, 3 Otomatik.
        public Task SetModeAsync(short mode)
        {
            return _queue.Enqueue(() => _plc.WriteIntTag(PlcTags.Mode, mode));
        }

        // ---------------- Reset (basılı tutma) ----------------
        // Bas: Mode = 1 (Güvenli) + HMI_Reset = TRUE
        // Bırak: HMI_Reset = FALSE
        // PLC: (Mode = 1 VEYA 3) VE HMI_Reset → TP 250 ms → Reset.

        public Task PressResetAsync()
        {
            return _queue.Enqueue(() =>
            {
                _plc.WriteIntTag(PlcTags.Mode, MachineModes.Guvenli);
                _plc.WriteBoolTag(PlcTags.Reset, true);
            });
        }

        // FALSE önemli (23 ve 58–71 numaralı alarmlar HMI_Reset TRUE iken
        // mühürlenemez): başarısız olursa birkaç kez denenir.
        public Task ReleaseResetAsync()
        {
            return ReleaseAsync(PlcTags.Reset);
        }

        // ---------------- Start / Stop (basılı tutma) ----------------

        public Task PressStartAsync()
        {
            return _queue.Enqueue(() => _plc.WriteBoolTag(PlcTags.Start, true));
        }

        public Task ReleaseStartAsync()
        {
            return ReleaseAsync(PlcTags.Start);
        }

        public Task PressStopAsync()
        {
            return _queue.Enqueue(() => _plc.WriteBoolTag(PlcTags.Stop, true));
        }

        public Task ReleaseStopAsync()
        {
            return ReleaseAsync(PlcTags.Stop);
        }

        // ---------------- Vardiya adedi sıfırla (basılı tutma) ----------------

        public Task PressShiftResetAsync()
        {
            return _queue.Enqueue(() => _plc.WriteBoolTag(PlcTags.VardiyaReset, true));
        }

        public Task ReleaseShiftResetAsync()
        {
            return ReleaseAsync(PlcTags.VardiyaReset);
        }

        // ---------------- Boşaltma (kalıcı aç/kapa) ----------------

        public Task SetBosaltmaModuAsync(bool on)
        {
            return _queue.Enqueue(() => _plc.WriteBoolTag(PlcTags.BosaltmaModu, on));
        }

        public Task SetBosaltmaBaslaAsync(bool on)
        {
            return _queue.Enqueue(() => _plc.WriteBoolTag(PlcTags.BosaltmaBasla, on));
        }

        // ---------------- İstasyon ayarları ----------------
        // Parametre ve kalıcı (aç/kapa) buton yazmaları. Tag adları
        // PlcTags'ten gelir; yalnızca operatör değer girince / butona
        // basınca çağrılır.

        public Task WriteRealAsync(string tag, float value)
        {
            return _queue.Enqueue(() => _plc.WriteRealTag(tag, value));
        }

        public Task WriteIntAsync(string tag, short value)
        {
            return _queue.Enqueue(() => _plc.WriteIntTag(tag, value));
        }

        public Task SetBoolAsync(string tag, bool value)
        {
            return _queue.Enqueue(() => _plc.WriteBoolTag(tag, value));
        }

        // Basılı tutma (push) butonu: bas → TRUE, bırak → FALSE (FALSE
        // başarısız olursa tekrar denenir).
        public Task PressMomentaryAsync(string tag)
        {
            return _queue.Enqueue(() => _plc.WriteBoolTag(tag, true));
        }

        public Task ReleaseMomentaryAsync(string tag)
        {
            return ReleaseAsync(tag);
        }

        // Basılı tutma bitini FALSE'a çeker; hata olursa tekrar dener.
        private Task ReleaseAsync(string tag)
        {
            return _queue.Enqueue(() =>
            {
                Exception last = null;

                for (int attempt = 1; attempt <= ReleaseRetryCount; attempt++)
                {
                    try
                    {
                        _plc.WriteBoolTag(tag, false);
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        AppLogger.Warn($"{tag} = FALSE yazılamadı (deneme {attempt}/{ReleaseRetryCount}): {ex.Message}");
                        Thread.Sleep(300);
                    }
                }

                throw last;
            });
        }
    }
}
