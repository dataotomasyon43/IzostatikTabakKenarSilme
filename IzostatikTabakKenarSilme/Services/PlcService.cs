
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

        public bool IsConnected { get; private set; }

        public void Connect()
        {
            Connect(DefaultIp);
        }

        public void Connect(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("PLC IP adresi boş olamaz.", nameof(ip));

            if (IsConnected && _ip == ip)
                return;

            Disconnect();

            _ip = ip.Trim();
            IsConnected = true;
        }

        private Tag Get(string name, int elementSize, int elementCount = 1)
        {
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
            Tag tag = Get(tagName, 1);
            tag.Read();
            return tag.GetUInt8(0) != 0;
        }

        // INT: 16 bit signed
        public short ReadIntTag(string tagName)
        {
            Tag tag = Get(tagName, 2);
            tag.Read();
            return tag.GetInt16(0);
        }

        // DINT: 32 bit signed
        public int ReadDIntTag(string tagName)
        {
            Tag tag = Get(tagName, 4);
            tag.Read();
            return tag.GetInt32(0);
        }

        // UDINT: 32 bit unsigned
        public uint ReadUDintTag(string tagName)
        {
            Tag tag = Get(tagName, 4);
            tag.Read();
            return tag.GetUInt32(0);
        }

        // REAL: 32 bit floating point
        public float TestReadReal(string tagName)
        {
            Tag tag = Get(tagName, 4);
            tag.Read();
            return tag.GetFloat32(0);
        }

        public float ReadPosition()
        {
            return TestReadReal("HMI_GTR_Akt_Poz");
        }

        // LREAL: 64 bit floating point
        public double ReadLRealTag(string tagName)
        {
            Tag tag = Get(tagName, 8);
            tag.Read();
            return tag.GetFloat64(0);
        }

        // BOOL dizisi
        public bool[] ReadAlarmArray(string tagName, int count, int chunk = 100)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            var result = new bool[count];

            for (int start = 0; start < count; start += chunk)
            {
                int n = Math.Min(chunk, count - start);

                // "Alm[100]" gibi, belirli indexten başlayan dilim
                Tag tag = Get($"{tagName}[{start}]", 1, n);
                tag.Read();

                for (int i = 0; i < n; i++)
                    result[start + i] = tag.GetUInt8(i) != 0;
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
            foreach (Tag tag in _tags.Values)
                tag.Dispose();

            _tags.Clear();
            IsConnected = false;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
