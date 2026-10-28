using System;
using System.Collections.Generic;

namespace IzostatikTabakKenarSilme.Services
{
    /// <summary>Aktif Alarmlar'da turuncu gösterilen sistem uyarısı (geçmişe yazılmaz).</summary>
    public sealed class SystemWarning
    {
        public string Key { get; set; }       // "_MC_ErrSta" gibi
        public string Kaynak { get; set; }    // "MC"
        public string Metin { get; set; }
        public ushort Kod { get; set; }
        public DateTime Baslangic { get; set; }

        public string KodText => $"0x{Kod:X4}";
        public string BaslangicText => Baslangic.ToString("dd.MM.yyyy HH:mm:ss");
    }

    /// <summary>
    /// Omron NJ hata durum kelimeleri (_MC_ErrSta, _EC_ErrSta, _PLC_ErrSta).
    /// Bit düzeni (Omron hata durum değişkenleri):
    ///   bit 15: hata modülün kendisinde
    ///   bit 14: bağlı bir eksen / slave'de hata (toplu durum)
    ///   bit 7: ağır hata, bit 6: kısmi hata, bit 5: hafif hata, bit 4: gözlem
    /// Ayrıntı (hangi eksen, olay kodu) Sysmac Studio > Sorun Giderme'de
    /// görülür; eksen değişkenleri HMI'dan okunamıyor.
    /// </summary>
    public static class SystemStatus
    {
        public sealed class Source
        {
            public Source(string tag, string shortName, string name, string child)
            {
                Tag = tag;
                ShortName = shortName;
                Name = name;
                Child = child;
            }

            public string Tag { get; }
            public string ShortName { get; }
            public string Name { get; }
            public string Child { get; }  // bit 14 için: "bir eksende"
        }

        public static readonly IReadOnlyList<Source> Sources = new[]
        {
            new Source("_MC_ErrSta", "MC", "Hareket kontrol (servo eksenleri)", "bir eksende / eksen grubunda"),
            new Source("_EC_ErrSta", "EC", "EtherCAT haberleşmesi", "bir EtherCAT slave'inde"),
            new Source("_PLC_ErrSta", "PLC", "PLC işlevi", "bağlı bir birimde"),
        };

        /// <summary>Durum kelimesi hata göstermiyorsa null.</summary>
        public static SystemWarning Describe(Source source, ushort status)
        {
            if (status == 0)
                return null;

            string level =
                (status & 0x0080) != 0 ? "ağır hata" :
                (status & 0x0040) != 0 ? "kısmi hata" :
                (status & 0x0020) != 0 ? "hafif hata" :
                (status & 0x0010) != 0 ? "gözlem (uyarı)" :
                "durum";

            var where = new List<string>();
            if ((status & 0x4000) != 0)
                where.Add(source.Child);
            if ((status & 0x8000) != 0)
                where.Add("modülün kendisinde");

            string text = $"{source.Name}: {level}" +
                          (where.Count > 0 ? " — " + string.Join(", ", where) : "") +
                          ". Ayrıntı: Sysmac Studio > Sorun Giderme.";

            return new SystemWarning
            {
                Key = source.Tag,
                Kaynak = source.ShortName,
                Metin = text,
                Kod = status
            };
        }
    }
}
