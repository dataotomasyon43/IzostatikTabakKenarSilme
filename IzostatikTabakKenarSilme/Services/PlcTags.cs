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

        // ---------------- AYARLAR: Transfer Yükleme (GTR) ----------------
        // Parametreler
        public const string GtrHizBos = "HMI_GTR_Oto_Bos_Hiz";            // INT, %
        public const string GtrIvmeBos = "HMI_GTR_Oto_Bos_Acc";           // INT, %
        public const string GtrHizDolu = "HMI_GTR_Oto_Dolu_Hiz";          // INT, %
        public const string GtrIvmeDolu = "HMI_GTR_Oto_Dolu_Acc";         // INT, %
        public const string GtrAlmaPoz = "HMI_GTR_Tabak_Alma_Pos";        // REAL, mm
        public const string GtrBirakmaPoz = "HMI_GTR_Tabak_Birakma_Pos";  // REAL, mm
        public const string GtrVakumSure = "HMI_GTR_Vakum_T";             // REAL, s
        public const string GtrUflemeSure = "HMI_GTR_Ufleme_T";           // REAL, s
        public const string GtrPrestenAlmaSure = "HMI_GTR_Tabak_Alma_T";  // REAL, s
        // Göstergeler (yalnız okunur)
        public const string GtrAktPoz = "HMI_GTR_Akt_Poz";                // REAL, mm
        public const string GtrPiston1Yukarida = "I_GTR_Pist1_Yukarida";
        public const string GtrPiston1Baski = "I_GTR_Pist1_Baski";
        public const string GtrPiston2Yukarida = "I_GTR_Pist2_Yukarida";
        public const string GtrPiston2Baski = "I_GTR_Pist2_Baski";
        // Basılı tutma (bas TRUE, bırak FALSE)
        public const string GtrAlmaPozOgren = "HMI_GTR_Tabak_Alma_Poz_Ogren";
        public const string GtrBirakmaPozOgren = "HMI_GTR_Tabak_Birakma_Poz_Ogren";
        public const string GtrAlmayaGit = "HMI_GTR_Alma_PozGit";
        public const string GtrBirakmayaGit = "HMI_GTR_Birakma_PozGit";
        public const string GtrKonveyore = "HMI_GTR_Konvde";
        public const string GtrGobege = "HMI_GTR_Tablada";
        // Kalıcı (bir basış TRUE, bir basış FALSE)
        public const string GtrPiston1Asagi = "HMI_GTR_Piston_Asagi_V_Man";
        public const string GtrPiston2Asagi = "HMI_GTR_Piston2_Asagi_V_Man";
        public const string GtrPiston1Vakum = "HMI_GTR_Piston1_Vakum_Man";
        public const string GtrPiston2Vakum = "HMI_GTR_Piston2_Vakum_Man";
        public const string GtrPiston1Ufleme = "HMI_GTR_Piston1_Ufleme_Man";
        public const string GtrPiston2Ufleme = "HMI_GTR_Piston2_Ufleme_Man";

        // ---------------- AYARLAR: Döner Tabla ----------------
        public const string GobekHiz = "HMI_Doner_Tabla_Hiz";            // INT, % (0-99)
        public const string TablaYerinde1 = "I_Tabla_Yerinde";           // gösterge
        // Basılı tutma (bas TRUE, bırak FALSE)
        public const string GobekAdim = "HMI_DonerTablaMan";
        // Kalıcı (bir basış TRUE, bir basış FALSE)
        public const string Baski1Asagi = "HMI_Tabla1_Baski_V_Man";
        public const string Baski2Asagi = "HMI_Tabla2_Baski_V_Man";
        public const string Baski3Asagi = "HMI_Tabla3_Baski_V_Man";

        // ---------------- AYARLAR: Giriş Çıkış Bomse ----------------
        // Giriş bomse = HMI_Bomse[1], çıkış bomse = HMI_Bomse[8].
        public const string GirisBomseOffset = "HMI_BomIn_Alma_Pos";      // REAL, ° (alma offset)
        public const string CikisBomseOffset = "HMI_BomOut_Alma_Pos";     // REAL, ° (referans offset)
        public const string GirisBomseAktPoz = "HMI_Bomse[1].Akt_Poz";    // REAL, °, gösterge
        public const string CikisBomseAktPoz = "HMI_Bomse[8].Akt_Poz";    // REAL, °, gösterge
        // Basılı tutma (jog: bas TRUE, bırak FALSE)
        public const string GirisBomseIleri = "HMI_Bomse[1].JogP";
        public const string GirisBomseGeri = "HMI_Bomse[1].JogN";
        public const string CikisBomseIleri = "HMI_Bomse[8].JogP";
        public const string CikisBomseGeri = "HMI_Bomse[8].JogN";

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
