namespace IzostatikTabakKenarSilme.Services
{
    // Uygulamada kullanılan PLC tag adları tek yerde.
    // Kaynak: Sysmac Studio global değişken listesi (omrontag2.csv).
    public static class PlcTags
    {
        // INT — 0 Kapalı, 1 Güvenli Mod, 2 Manuel Mod, 3 Otomatik Mod
        public const string Mode = "Mode";

        // BOOL — makine çalışıyor (otomatik çevrim aktif)
        public const string AutoCycle = "AutoCycle";

        // BOOL — otomatik çevrim başlat / durdur (basılı tutma).
        // PLC: AutoCycle = Otomatik VE (Baslat VEYA AutoCycle) VE Durdur değil.
        // Baslat TRUE kalırsa Durdur bırakılınca çevrim yeniden başlar;
        // Durdur TRUE kalırsa çevrim başlamaz → ikisi de bırakınca FALSE.
        public const string Start = "HMI_Sistem_Baslat";
        public const string Stop = "HMI_Sistem_Durdur";

        // BOOL — alarm reset (basılı tutma: TRUE, bırakınca FALSE)
        public const string Reset = "HMI_Reset";

        // BOOL[0..500] — alarm dizisi
        public const string AlarmArray = "Alm";
        public const int AlarmCount = 501;
    }

    // Mode tag'inin değerleri.
    public static class MachineModes
    {
        public const short Kapali = 0;
        public const short Guvenli = 1;
        public const short Manuel = 2;
        public const short Otomatik = 3;

        public static string ToText(short mode)
        {
            switch (mode)
            {
                case Kapali: return "Kapalı";
                case Guvenli: return "Güvenli Mod";
                case Manuel: return "Manuel Mod";
                case Otomatik: return "Otomatik Mod";
                default: return $"Bilinmeyen ({mode})";
            }
        }
    }
}
