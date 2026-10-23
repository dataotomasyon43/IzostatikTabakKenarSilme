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

        // Üretim adetleri (Vardiya_Adetler: CTR_Step = 110 olunca ikisi de +1)
        public const string VardiyaAdet = "HMI_Vardiya_Adet";     // INT
        public const string ToplamAdet = "HMI_Toplam_Adet";       // UDINT
        // BOOL — vardiya sayacı reset (CTU Reset girişi; PLC sıfırlamaz →
        // basılı tutma: TRUE, bırakınca FALSE)
        public const string VardiyaReset = "HMI_Vardiya_Reset";

        // Boşaltma (Genel_Prog rung 12–14). Kalıcı (aç/kapa) butonlar:
        // bir basış TRUE, bir basış FALSE.
        //   HMI_Rotus_ON: boşaltma modu açık
        //   HMI_Konvde_Tabak_Var: boşaltma başla (PLC 8 sayımdan sonra sıfırlar)
        public const string BosaltmaModu = "HMI_Rotus_ON";
        public const string BosaltmaBasla = "HMI_Konvde_Tabak_Var";

        // ---------------- AYARLAR: Vibrasyon Merkezleme ----------------
        public const string VibrasyonSure = "HMI_Vibrasyon_T";                 // REAL, sn
        public const string VibrasyonSonrasiBekleme = "HMI_Vibrasyon_Sonrasi_T"; // REAL, sn
        public const string VibrasyonHiz = "HMI_Vibrasyon_Hiz";                // INT, %
        // BOOL — manuel çalıştırma, kalıcı (bir basış TRUE, bir basış FALSE)
        public const string VibrasyonManStart = "HMI_Vibrasyon_Man_Start";
        // Göstergeler (yalnız okunur)
        public const string VibrasyonCalisiyor = "Q_Vibrasyon_Calis";
        public const string MerkezlemeSensor = "I_Merkezleme_Sens";
        public const string VibrasyonInvHata = "I_Vib_Inv_Hata";

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
