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

        // ---------------- AYARLAR: Çapak Kırma (istasyon 2, Bomse 2) ----------------
        public const string CpkBomseBasPoz = "HMI_BomseCpk_BasPoz";            // REAL, °
        public const string CpkBomseAktPoz = "HMI_Bomse[2].Akt_Poz";           // REAL, °, gösterge
        public const string CpkBitisAci = "HMI_Ist2BitisAci";                  // REAL, °
        public const string CpkModGeriCekilmeAci = "HMI_Ist2ModGeriCekilmeAci"; // REAL, °
        public const string CpkTurSure = "HMI_Tur2Sure";                       // REAL, s
        public const string CpkTurSayi = "HMI_Ist2TurSayi";                    // INT, adet
        public const string CpkBomseGeriYon = "HMI_Bomse2Yon";                 // BOOL, kalıcı ("geri")
        public const string CpkModHiz = "HMI_CpkMod_OtoHiz";                   // INT, %
        public const string CpkModAktPoz = "HMI_CpkMod_ActPos";                // REAL, mm, gösterge
        public const string CpkModBasPoz = "HMI_CpkMod_BasPoz";                // REAL, mm
        // Basılı tutma (bas TRUE, bırak FALSE)
        public const string CpkTeach = "HMI_Cpk_BasPoz_Ogren";
        public const string CpkBomseIleri = "HMI_Bomse[2].JogP";
        public const string CpkBomseGeri = "HMI_Bomse[2].JogN";
        public const string CpkModulIleri = "HMI_Cpk_Modul_PJog";
        public const string CpkModulGeri = "HMI_Cpk_Modul_NJog";

        // ---------------- AYARLAR: Zımpara-1 (istasyon 3, Bomse 3) ----------------
        public const string Zim1BomseBasPoz = "HMI_BomseZim1_BasPoz";            // REAL, °
        public const string Zim1BomseAktPoz = "HMI_Bomse[3].Akt_Poz";            // REAL, °, gösterge
        public const string Zim1BitisAci = "HMI_Ist3BitisAci";                   // REAL, °
        public const string Zim1ModGeriCekilmeAci = "HMI_Ist3ModGeriCekilmeAci"; // REAL, °
        public const string Zim1TurSure = "HMI_Tur3Sure";                        // REAL, s
        public const string Zim1TurSayi = "HMI_Ist3TurSayi";                     // INT, adet
        public const string Zim1BomseYon = "HMI_Bomse3Yon";                      // BOOL, kalıcı
        public const string Zim1BomseJogP = "HMI_Bomse[3].JogP";                 // basılı tutma
        public const string Zim1BomseJogN = "HMI_Bomse[3].JogN";                 // basılı tutma
        public const string Zim1TaslamaHiz = "HMI_Zimpara1_Hiz";                 // INT, % (0-99)
        public const string Zim1TaslamaYon = "HMI_Zimpara1_Yon";                 // BOOL, kalıcı
        public const string Zim1ManStart = "HMI_Zimpara1_Man_Start";             // BOOL, kalıcı
        public const string Zim1ModHiz = "HMI_Zim1Mod_OtoHiz";                   // INT, %
        public const string Zim1ModAktPoz = "HMI_Zim1Mod_ActPos";                // REAL, mm, gösterge
        public const string Zim1ModBasPoz = "HMI_Zim1_BasPoz";                   // REAL, mm
        public const string Zim1Teach = "HMI_Zim1_BasPoz_Ogren";                 // basılı tutma
        public const string Zim1ModJogP = "HMI_Zim1_Modul_PJog";                 // basılı tutma
        public const string Zim1ModJogN = "HMI_Zim1_Modul_NJog";                 // basılı tutma

        // ---------------- AYARLAR: Zımpara-2 (istasyon 4, Bomse 4) ----------------
        public const string Zim2BomseBasPoz = "HMI_BomseZim2_BasPoz";            // REAL, °
        public const string Zim2BomseAktPoz = "HMI_Bomse[4].Akt_Poz";            // REAL, °, gösterge
        public const string Zim2BitisAci = "HMI_Ist4BitisAci";                   // REAL, °
        public const string Zim2ModGeriCekilmeAci = "HMI_Ist4ModGeriCekilmeAci"; // REAL, °
        public const string Zim2TurSure = "HMI_Tur4Sure";                        // REAL, s
        public const string Zim2TurSayi = "HMI_Ist4TurSayi";                     // INT, adet
        public const string Zim2BomseYon = "HMI_Bomse4Yon";                      // BOOL, kalıcı
        public const string Zim2BomseJogP = "HMI_Bomse[4].JogP";                 // basılı tutma
        public const string Zim2BomseJogN = "HMI_Bomse[4].JogN";                 // basılı tutma
        public const string Zim2TaslamaHiz = "HMI_Zimpara2_Hiz";                 // INT, % (0-99)
        public const string Zim2TaslamaYon = "HMI_Zimpara2_Yon";                 // BOOL, kalıcı
        public const string Zim2ManStart = "HMI_Zimpara2_Man_Start";             // BOOL, kalıcı
        public const string Zim2ModHiz = "HMI_Zim2Mod_OtoHiz";                   // INT, %
        public const string Zim2ModAktPoz = "HMI_Zim2Mod_ActPos";                // REAL, mm, gösterge
        public const string Zim2ModBasPoz = "HMI_Zim2_BasPoz";                   // REAL, mm
        public const string Zim2Teach = "HMI_Zim2_BasPoz_Ogren";                 // basılı tutma
        public const string Zim2ModJogP = "HMI_Zim2_Modul_PJog";                 // basılı tutma
        public const string Zim2ModJogN = "HMI_Zim2_Modul_NJog";                 // basılı tutma

        // ---------------- AYARLAR: Sünger-1 (istasyon 5, Bomse 5) ----------------
        public const string Sun1BomseBasPoz = "HMI_BomseSun1_BasPoz";            // REAL, °
        public const string Sun1BomseAktPoz = "HMI_Bomse[5].Akt_Poz";            // REAL, °, gösterge
        public const string Sun1BitisAci = "HMI_Ist5BitisAci";                   // REAL, °
        public const string Sun1ModGeriCekilmeAci = "HMI_Ist5ModGeriCekilmeAci"; // REAL, °
        public const string Sun1TurSure = "HMI_Tur5Sure";                        // REAL, s
        public const string Sun1TurSayi = "HMI_Ist5TurSayi";                     // INT, adet
        public const string Sun1BomseYon = "HMI_Bomse5Yon";                      // BOOL, kalıcı
        public const string Sun1BomseJogP = "HMI_Bomse[5].JogP";                 // basılı tutma
        public const string Sun1BomseJogN = "HMI_Bomse[5].JogN";                 // basılı tutma
        public const string Sun1SungerHiz = "HMI_Sunger1_Hiz";                   // INT, % (0-99)
        public const string Sun1SungerYon = "HMI_Sunger1_Yon";                   // BOOL, kalıcı
        public const string Sun1ManStart = "HMI_Sunger1_Man_Start";              // BOOL, kalıcı
        public const string Sun1ModHiz = "HMI_Sun1Mod_OtoHiz";                   // INT, %
        public const string Sun1ModAktPoz = "HMI_Sun1Mod_ActPos";                // REAL, mm, gösterge
        public const string Sun1ModBasPoz = "HMI_Sun1_BasPoz";                   // REAL, mm
        public const string Sun1Teach = "HMI_Sun1_BasPoz_Ogren";                 // basılı tutma
        public const string Sun1ModJogP = "HMI_Sun1_Modul_PJog";                 // basılı tutma
        public const string Sun1ModJogN = "HMI_Sun1_Modul_NJog";                 // basılı tutma

        // ---------------- AYARLAR: Sünger-2 (istasyon 6, Bomse 6) ----------------
        public const string Sun2BomseBasPoz = "HMI_BomseSun2_BasPoz";            // REAL, °
        public const string Sun2BomseAktPoz = "HMI_Bomse[6].Akt_Poz";            // REAL, °, gösterge
        public const string Sun2BitisAci = "HMI_Ist6BitisAci";                   // REAL, °
        public const string Sun2ModGeriCekilmeAci = "HMI_Ist6ModGeriCekilmeAci"; // REAL, °
        public const string Sun2TurSure = "HMI_Tur6Sure";                        // REAL, s
        public const string Sun2TurSayi = "HMI_Ist6TurSayi";                     // INT, adet
        public const string Sun2BomseYon = "HMI_Bomse6Yon";                      // BOOL, kalıcı
        public const string Sun2BomseJogP = "HMI_Bomse[6].JogP";                 // basılı tutma
        public const string Sun2BomseJogN = "HMI_Bomse[6].JogN";                 // basılı tutma
        public const string Sun2SungerHiz = "HMI_Sunger2_Hiz";                   // INT, % (0-99)
        public const string Sun2SungerYon = "HMI_Sunger2_Yon";                   // BOOL, kalıcı
        public const string Sun2ManStart = "HMI_Sunger2_Man_Start";              // BOOL, kalıcı
        public const string Sun2ModHiz = "HMI_Sun2Mod_OtoHiz";                   // INT, %
        public const string Sun2ModAktPoz = "HMI_Sun2Mod_ActPos";                // REAL, mm, gösterge
        public const string Sun2ModBasPoz = "HMI_Sun2_BasPoz";                   // REAL, mm
        public const string Sun2Teach = "HMI_Sun2_BasPoz_Ogren";                 // basılı tutma
        public const string Sun2ModJogP = "HMI_Sun2_Modul_PJog";                 // basılı tutma
        public const string Sun2ModJogN = "HMI_Sun2_Modul_NJog";                 // basılı tutma

        // ---------------- AYARLAR: Sünger-3 (istasyon 7, Bomse 7) ----------------
        public const string Sun3BomseBasPoz = "HMI_BomseSun3_BasPoz";            // REAL, °
        public const string Sun3BomseAktPoz = "HMI_Bomse[7].Akt_Poz";            // REAL, °, gösterge
        public const string Sun3BitisAci = "HMI_Ist7BitisAci";                   // REAL, °
        public const string Sun3ModGeriCekilmeAci = "HMI_Ist7ModGeriCekilmeAci"; // REAL, °
        public const string Sun3TurSure = "HMI_Tur7Sure";                        // REAL, s
        public const string Sun3TurSayi = "HMI_Ist7TurSayi";                     // INT, adet
        public const string Sun3BomseYon = "HMI_Bomse7Yon";                      // BOOL, kalıcı
        public const string Sun3BomseJogP = "HMI_Bomse[7].JogP";                 // basılı tutma
        public const string Sun3BomseJogN = "HMI_Bomse[7].JogN";                 // basılı tutma
        public const string Sun3SungerHiz = "HMI_Sunger3_Hiz";                   // INT, % (0-99)
        public const string Sun3SungerYon = "HMI_Sunger3_Yon";                   // BOOL, kalıcı
        public const string Sun3ManStart = "HMI_Sunger3_Man_Start";              // BOOL, kalıcı
        public const string Sun3ModHiz = "HMI_Sun3Mod_OtoHiz";                   // INT, %
        public const string Sun3ModAktPoz = "HMI_Sun3Mod_ActPos";                // REAL, mm, gösterge
        public const string Sun3ModBasPoz = "HMI_Sun3_BasPoz";                   // REAL, mm
        public const string Sun3Teach = "HMI_Sun3_BasPoz_Ogren";                 // basılı tutma
        public const string Sun3ModJogP = "HMI_Sun3_Modul_PJog";                 // basılı tutma
        public const string Sun3ModJogN = "HMI_Sun3_Modul_NJog";                 // basılı tutma

        // ---------------- AYARLAR: Transfer Boşaltma (CTR) ----------------
        // Parametreler
        public const string CtrHizBos = "HMI_CTR_Oto_Bos_Hiz";            // INT, %
        public const string CtrIvmeBos = "HMI_CTR_Oto_Bos_Acc";           // INT, %
        public const string CtrHizDolu = "HMI_CTR_Oto_Dolu_Hiz";          // INT, %
        public const string CtrIvmeDolu = "HMI_CTR_Oto_Dolu_Acc";         // INT, %
        public const string CtrAlmaPoz = "HMI_CTR_Tabak_Alma_Pos";        // REAL, mm
        public const string CtrBirakmaPoz = "HMI_CTR_Tabak_Birakma_Pos";  // REAL, mm
        public const string CtrVakumSure = "HMI_CTR_Vakum_T";             // REAL, s
        public const string CtrUflemeSure = "HMI_CTR_Ufleme_T";           // REAL, s
        // Göstergeler (yalnız okunur)
        public const string CtrAktPoz = "HMI_CTR_Akt_Poz";                // REAL, mm
        public const string CtrPiston1Yukarida = "I_CTR_Piston_Yukarida";
        public const string CtrPiston1Baski = "I_CTR_Piston_Baski";
        // Basılı tutma (bas TRUE, bırak FALSE)
        public const string CtrAlmaPozOgren = "HMI_CTR_Tabak_Alma_Poz_Ogren";
        public const string CtrBirakmaPozOgren = "HMI_CTR_Tabak_Birakma_Poz_Ogren";
        public const string CtrAlmayaGit = "HMI_CTR_Alma_PozGit";
        public const string CtrBirakmayaGit = "HMI_CTR_Birakma_PozGit";
        public const string CtrGobege = "HMI_CTR_Tablada";
        public const string CtrIstifTablaya = "HMI_CTR_Konvde";
        // Kalıcı (bir basış TRUE, bir basış FALSE)
        public const string CtrPiston1Asagi = "HMI_CTR_Piston_Asagi_V_Man";
        public const string CtrPiston1Vakum = "HMI_CTR_Piston_Vakum_Man";
        public const string CtrPiston1Ufleme = "HMI_CTR_Piston_Ufleme_Man";

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
