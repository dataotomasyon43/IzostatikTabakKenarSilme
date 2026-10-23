using System;
using System.Linq;
using System.Threading;
using HidSharp;

namespace IzostatikTabakKenarSilme.Services
{
    // Lamba modu (output raporunda her lambanın 4. byte'ı).
    public enum LampMode : byte
    {
        Sabit = 0x01,
        FlasHizli = 0x02,
        FlasOrta = 0x03,
        FlasYavas = 0x04
    }

    // Pro-face EZ Illumination Switch (PFXZCCEUSG1) — 5 buton, 5 RGB lamba.
    // USB HID, VID 0x114E / PID 0x0013, sürücüsüz.
    //
    // Protokol (deneme yoluyla çözülmüş, resmi değil):
    //   Input  9 byte : [0]=ReportID 0x00, [1..5]=buton 1..5 (0 = bırakık)
    //                   Rapor yalnızca durum değişince gelir.
    //   Output 33 byte: [0]=ReportID 0x00, lamba N: [1+(N-1)*4 .. +3] = R, G, B, Mod
    //
    // Bu cihaz GÜVENLİK fonksiyonu (acil stop vb.) için kullanılmaz.
    //
    // Olaylar (ButtonChanged, ConnectionChanged) arka plan thread'inden
    // gelir; WPF tarafında Dispatcher ile UI thread'ine aktarılmalıdır.
    // Cihaz çıkarılıp takılırsa (Windows cihaz listesi değişince) otomatik
    // yeniden bağlanır ve son lamba durumunu geri yükler.
    public sealed class EzSwitchDevice : IDisposable
    {
        public const int VendorId = 0x114E;
        public const int ProductId = 0x0013;
        public const int LampCount = 5;
        public const int ButtonCount = 5;

        private const int DefaultInputLength = 9;
        private const int DefaultOutputLength = 33;

        // _stream ve _lamps bu kilitle korunur.
        private readonly object _sync = new object();

        // Lamba tamponu (Report ID dahil 33 byte). Her raporda tüm lambalar
        // birlikte gönderildiği için durum burada tutulur.
        private readonly byte[] _lamps = new byte[DefaultOutputLength];

        private readonly bool[] _pressed = new bool[ButtonCount];

        private HidStream _stream;
        private int _inputLength = DefaultInputLength;
        private int _outputLength = DefaultOutputLength;
        private bool _started;
        private bool _disposed;

        // (buton 1..5, basılı mı)
        public event Action<int, bool> ButtonChanged;

        // true: bağlandı, false: bağlantı koptu
        public event Action<bool> ConnectionChanged;

        public bool IsConnected
        {
            get { lock (_sync) return _stream != null; }
        }

        public EzSwitchDevice()
        {
            // Başlangıç: tüm lambalar sönük (R=G=B=0, Mod=Sabit — güvenli söndürme)
            for (int no = 1; no <= LampCount; no++)
                _lamps[LampOffset(no) + 3] = (byte)LampMode.Sabit;
        }

        // Cihazı aramaya başlar. Takılı değilse, takıldığında bağlanır.
        public void Start()
        {
            if (_started)
                return;

            _started = true;
            DeviceList.Local.Changed += DeviceList_Changed;
            TryConnect();
        }

        private void DeviceList_Changed(object sender, DeviceListChangedEventArgs e)
        {
            if (!IsConnected)
                TryConnect();
        }

        private void TryConnect()
        {
            HidStream stream = null;

            lock (_sync)
            {
                if (_disposed || _stream != null)
                    return;

                try
                {
                    HidDevice device = DeviceList.Local
                        .GetHidDevices(VendorId, ProductId)
                        .FirstOrDefault();

                    if (device == null)
                        return;

                    if (!device.TryOpen(out stream))
                    {
                        AppLogger.Warn("EZ buton modülü bulundu ama açılamadı (başka bir program kullanıyor olabilir).");
                        return;
                    }

                    _inputLength = SafeLength(device.GetMaxInputReportLength(), DefaultInputLength);
                    _outputLength = SafeLength(device.GetMaxOutputReportLength(), DefaultOutputLength);

                    stream.ReadTimeout = Timeout.Infinite;
                    _stream = stream;

                    // Yeniden bağlanmada son lamba durumunu geri yükle.
                    WriteCore();
                }
                catch (Exception ex)
                {
                    // Yarım kalan bağlantıyı geri al; cihaz yeniden takılınca
                    // (DeviceList.Changed) tekrar denenir.
                    if (stream != null && ReferenceEquals(_stream, stream))
                        _stream = null;

                    try { stream?.Dispose(); } catch { }

                    AppLogger.Error("EZ buton modülüne bağlanılamadı", ex);
                    return;
                }
            }

            var thread = new Thread(() => ReadLoop(stream))
            {
                IsBackground = true,
                Name = "EzSwitchRead"
            };
            thread.Start();

            AppLogger.Info($"EZ buton modülü bağlandı (input {_inputLength} / output {_outputLength} byte).");
            Raise(ConnectionChanged, true);
        }

        private static int SafeLength(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        // Butonları okur. Read, durum değişene kadar bekler (bloklar).
        private void ReadLoop(HidStream stream)
        {
            var buffer = new byte[Math.Max(_inputLength, DefaultInputLength)];

            try
            {
                while (true)
                {
                    int count = stream.Read(buffer, 0, buffer.Length);

                    if (count <= 0)
                        continue;

                    for (int i = 0; i < ButtonCount && 1 + i < count; i++)
                    {
                        // Değerin kendisine güvenme; yalnızca sıfır mı bak.
                        bool pressed = buffer[1 + i] != 0;

                        if (pressed != _pressed[i])
                        {
                            _pressed[i] = pressed;
                            RaiseButton(i + 1, pressed);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Disconnect(stream, ex);
            }
        }

        // Bağlantı koptu: stream kapatılır, basılı görünen butonlar için
        // "bırakıldı" üretilir, cihaz yeniden takılınca tekrar bağlanılır.
        private void Disconnect(HidStream stream, Exception reason)
        {
            bool wasCurrent;

            lock (_sync)
            {
                wasCurrent = ReferenceEquals(_stream, stream);

                if (wasCurrent)
                    _stream = null;
            }

            try { stream.Dispose(); } catch { }

            if (!wasCurrent || _disposed)
                return;

            for (int i = 0; i < ButtonCount; i++)
            {
                if (_pressed[i])
                {
                    _pressed[i] = false;
                    RaiseButton(i + 1, false);
                }
            }

            AppLogger.Warn($"EZ buton modülü bağlantısı koptu: {reason?.Message}");
            Raise(ConnectionChanged, false);

            // Yeniden bağlanma burada denenmez (arızalı cihazda sıkı döngüye
            // girmesin); cihaz çıkarılıp takılınca DeviceList.Changed tetikler.
        }

        // ---------------- Lambalar ----------------

        private static int LampOffset(int no)
        {
            return 1 + (no - 1) * 4;
        }

        // Lamba N (1..5) rengini ve modunu ayarlar. send=false ise yalnızca
        // tampon değişir; birden çok lambayı ayarlayıp tek Update() yapılır.
        public void SetLamp(int no, byte r, byte g, byte b, LampMode mode = LampMode.Sabit, bool send = true)
        {
            if (no < 1 || no > LampCount)
                throw new ArgumentOutOfRangeException(nameof(no));

            lock (_sync)
            {
                int o = LampOffset(no);
                _lamps[o] = r;
                _lamps[o + 1] = g;
                _lamps[o + 2] = b;
                _lamps[o + 3] = (byte)mode;
            }

            if (send)
                Update();
        }

        public void SetAll(byte r, byte g, byte b, LampMode mode = LampMode.Sabit)
        {
            for (int no = 1; no <= LampCount; no++)
                SetLamp(no, r, g, b, mode, send: false);

            Update();
        }

        // Güvenli söndürme: R=G=B=0, Mod=Sabit.
        public void AllOff()
        {
            SetAll(0, 0, 0, LampMode.Sabit);
        }

        // Lamba tamponunu cihaza gönderir. Bağlı değilse bir şey yapmaz;
        // bağlanınca tampon zaten gönderilir.
        public void Update()
        {
            HidStream failed = null;
            Exception error = null;

            lock (_sync)
            {
                try
                {
                    WriteCore();
                }
                catch (Exception ex)
                {
                    failed = _stream;
                    error = ex;
                }
            }

            if (failed != null)
                Disconnect(failed, error);
        }

        // Çağıran taraf _sync kilidini tutmalıdır.
        private void WriteCore()
        {
            if (_stream == null)
                return;

            var report = new byte[_outputLength];
            Array.Copy(_lamps, report, Math.Min(_lamps.Length, report.Length));
            report[0] = 0x00; // Report ID

            _stream.Write(report);
        }

        // ---------------- Olaylar ----------------

        // Olay işleyicisindeki bir hata okuma döngüsünü öldürmesin.
        private void RaiseButton(int button, bool pressed)
        {
            try
            {
                ButtonChanged?.Invoke(button, pressed);
            }
            catch (Exception ex)
            {
                AppLogger.Error("EZ buton olayı işlenirken hata", ex);
            }
        }

        private static void Raise(Action<bool> handler, bool value)
        {
            try
            {
                handler?.Invoke(value);
            }
            catch (Exception ex)
            {
                AppLogger.Error("EZ buton bağlantı olayı işlenirken hata", ex);
            }
        }

        public void Dispose()
        {
            HidStream stream;

            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                stream = _stream;
                _stream = null;
            }

            if (_started)
                DeviceList.Local.Changed -= DeviceList_Changed;

            // Stream kapatılınca bloklayan Read hata verir ve okuma thread'i biter.
            try { stream?.Dispose(); } catch { }
        }
    }
}
