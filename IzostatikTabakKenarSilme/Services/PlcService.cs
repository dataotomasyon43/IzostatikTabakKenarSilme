
using System;
using System.Collections.Generic;
using libplctag;

namespace IzostatikTabakKenarSilme.Services
{
    public sealed class PlcService : IDisposable
    {
        private const string DefaultIp = "192.168.26.10";
        private const string PlcPath = "1,0";

        private string _ip = DefaultIp;
        private readonly Dictionary<string, Tag> _tags =
            new Dictionary<string, Tag>();

        // Tüm PLC erişimleri bu kilitle sıraya alınır. Servis hem ana
        // penceredeki periyodik okumadan hem de sayfalardan, arka plan
        // thread'lerinden çağrılır; libplctag Tag nesneleri ve _tags
        // sözlüğü eşzamanlı kullanıma karşı korunur.
        private readonly object _sync = new object();
        private bool _disposed;

        // Not: Bu değer ağ bağlantısını değil, servisin kullanıma
        // hazırlandığını gösterir. Gerçek bağlantı durumu için son
        // okumanın başarılı olup olmadığına bakılmalıdır.
        public bool IsConnected { get; private set; }

        // Kilitsiz okunur: UI thread'i, uzun süren bir PLC okuması
        // kilidi tutarken burada beklememelidir.
        public string IpAddress => _ip;

        public void Connect()
        {
            Connect(DefaultIp);
        }

        public void Connect(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("PLC IP adresi boş olamaz.", nameof(ip));

            lock (_sync)
            {
                ThrowIfDisposed();

                if (IsConnected && _ip == ip)
                    return;

                DisconnectCore();

                _ip = ip.Trim();
                IsConnected = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcService));
        }

        // Çağıran taraf _sync kilidini tutmalıdır.
        private Tag Get(string name, int elementSize, int elementCount = 1)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tag adı boş olamaz.", nameof(name));

            if (!IsConnected)
                Connect();

            string key = $"{name}|{elementSize}|{elementCount}";

            if (!_tags.TryGetValue(key, out Tag tag))
            {
                tag = new Tag
                {
                    Name = name,
                    Gateway = _ip,
                    Path = PlcPath,
                    PlcType = PlcType.Omron,
                    Protocol = Protocol.ab_eip,
                    ElementSize = elementSize,
                    ElementCount = elementCount,
                    Timeout = TimeSpan.FromSeconds(3)
                };

                _tags.Add(key, tag);
            }

            return tag;
        }

        // BOOL: 1 byte
        public bool ReadBoolTag(string tagName)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 1);
                tag.Read();
                return tag.GetUInt8(0) != 0;
            }
        }

        // INT: 16 bit signed
        public short ReadIntTag(string tagName)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 2);
                tag.Read();
                return tag.GetInt16(0);
            }
        }

        // DINT: 32 bit signed
        public int ReadDIntTag(string tagName)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 4);
                tag.Read();
                return tag.GetInt32(0);
            }
        }

        // UDINT: 32 bit unsigned
        public uint ReadUDintTag(string tagName)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 4);
                tag.Read();
                return tag.GetUInt32(0);
            }
        }

        // REAL: 32 bit floating point
        public float ReadRealTag(string tagName)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 4);
                tag.Read();
                return tag.GetFloat32(0);
            }
        }

        public float TestReadReal(string tagName)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 4);
                tag.Read();
                return tag.GetFloat32(0);
            }
        }

        public float ReadPosition()
        {
            return TestReadReal("HMI_GTR_Akt_Poz");
        }

        // LREAL: 64 bit floating point
        public double ReadLRealTag(string tagName)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 8);
                tag.Read();
                return tag.GetFloat64(0);
            }
        }

        // ---------------- YAZMA ----------------
        // Yazma metotları yalnızca operatörün açık bir eylemiyle (mod
        // butonu, alarm reset) çağrılır; otomatik olarak hiçbir yerden
        // çağrılmaz.
        //
        // Yazmadan önce tag bir kez okunur: libplctag, Omron için yazma
        // isteğinde kullanılacak CIP veri tipini okuma yanıtından alır.
        // Okuma yapılmadan yazma bazı sürümlerde hata verebilir.

        // INT: 16 bit signed
        public void WriteIntTag(string tagName, short value)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 2);
                tag.Read();
                tag.SetInt16(0, value);
                tag.Write();
            }
        }

        // REAL: 32 bit floating point
        public void WriteRealTag(string tagName, float value)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 4);
                tag.Read();
                tag.SetFloat32(0, value);
                tag.Write();
            }
        }

        // REAL dizisi (ör. Rec1: REAL[0..59]). Tüm dizi tek istekte okunur.
        public float[] ReadRealArray(string tagName, int count)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 4, count);
                tag.Read();

                var values = new float[count];
                for (int i = 0; i < count; i++)
                    values[i] = tag.GetFloat32(i * 4);

                return values;
            }
        }

        // REAL dizisi yazma: tüm dizi tek istekte (önce okunur, bkz. yukarı).
        public void WriteRealArray(string tagName, float[] values)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("Yazılacak değer yok.", nameof(values));

            lock (_sync)
            {
                Tag tag = Get(tagName, 4, values.Length);
                tag.Read();

                for (int i = 0; i < values.Length; i++)
                    tag.SetFloat32(i * 4, values[i]);

                tag.Write();
            }
        }

        // LREAL: 64 bit floating point
        public void WriteLRealTag(string tagName, double value)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 8);
                tag.Read();
                tag.SetFloat64(0, value);
                tag.Write();
            }
        }

        // BOOL: 1 byte (okuma ile aynı eleman boyutu)
        public void WriteBoolTag(string tagName, bool value)
        {
            lock (_sync)
            {
                Tag tag = Get(tagName, 1);
                tag.Read();
                tag.SetUInt8(0, value ? (byte)1 : (byte)0);
                tag.Write();
            }
        }

        // BOOL dizisi
        public bool[] ReadAlarmArray(string tagName, int count, int chunk = 100)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            var result = new bool[count];

            // Dizinin tamamı tek istekte okunamıyor (ErrorBadParam);
            // indeksli parçalarla okuma mantığı aynen korunmuştur.
            // Kilit parça başına alınır; böylece 501 elemanlık okuma
            // sürerken ana penceredeki bağlantı kontrolü araya girebilir.
            for (int start = 0; start < count; start += chunk)
            {
                int n = Math.Min(chunk, count - start);

                lock (_sync)
                {
                    // "Alm[100]" gibi, belirli indexten başlayan dilim
                    Tag tag = Get($"{tagName}[{start}]", 1, n);
                    tag.Read();

                    for (int i = 0; i < n; i++)
                        result[start + i] = tag.GetUInt8(i) != 0;
                }
            }

            return result;
        }

        // Tek BOOL okuması için test metodu
        public bool ReadBoolArrayForTest(string tagName)
        {
            return ReadBoolTag(tagName);
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                DisconnectCore();
            }
        }

        // Çağıran taraf _sync kilidini tutmalıdır.
        private void DisconnectCore()
        {
            foreach (Tag tag in _tags.Values)
                tag.Dispose();

            _tags.Clear();
            IsConnected = false;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                DisconnectCore();
                _disposed = true;
            }
        }
    }
}
