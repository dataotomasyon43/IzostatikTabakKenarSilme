using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IzostatikTabakKenarSilme.Services.Recete
{
    public enum ReceteParamType
    {
        Real,
        Int,
        Bool
    }

    /// <summary>Reçeteye giren tek bir PLC ayarı.</summary>
    public sealed class ReceteParam
    {
        private static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        public ReceteParam(string group, string label, string tag, ReceteParamType type,
            string unit = "", int decimals = 0, double min = 0, double max = 9999)
        {
            Group = group;
            Label = label;
            Tag = tag;
            Type = type;
            Unit = unit;
            Decimals = decimals;
            Min = min;
            Max = max;
        }

        public string Group { get; }
        public string Label { get; }
        public string Tag { get; }
        public ReceteParamType Type { get; }
        public string Unit { get; }
        public int Decimals { get; }
        public double Min { get; }
        public double Max { get; }

        // BOOL: 0 / 1
        public bool InRange(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                return false;

            return Type == ReceteParamType.Bool ? (v == 0 || v == 1) : (v >= Min && v <= Max);
        }

        public string Format(double? v)
        {
            if (!v.HasValue)
                return "—";

            if (Type == ReceteParamType.Bool)
                return v.Value != 0 ? "AÇIK" : "KAPALI";

            string s = v.Value.ToString("F" + Decimals, Tr);
            return string.IsNullOrEmpty(Unit) ? s : s + " " + Unit;
        }

        // Ekranda görünen hassasiyette eşit mi (REAL yuvarlama farkları
        // "farklı" sayılmasın).
        public bool SameValue(double a, double b)
        {
            if (Type == ReceteParamType.Bool)
                return (a != 0) == (b != 0);

            double tol = 0.5 * Math.Pow(10, -Decimals);
            return Math.Abs(a - b) <= tol;
        }
    }

    /// <summary>
    /// Reçeteye giren ayarlar: tabağa göre değişen istasyon, servo,
    /// transfer ve çıkış ayarları. Tabaktan bağımsız makine ayarları
    /// (vibrasyon, döner tabla hızı, tork limitleri) girmez.
    /// Sınırlar ilgili ayar sayfalarındakilerle aynı.
    /// </summary>
    public static class ReceteParams
    {
        public static readonly IReadOnlyList<ReceteParam> All = Build();

        public static ReceteParam Find(string tag)
        {
            return All.FirstOrDefault(p => p.Tag == tag);
        }

        private static List<ReceteParam> Build()
        {
            var list = new List<ReceteParam>();

            // İstasyonlar
            Station(list, "Çapak Kırma", PlcTags.CpkBomseBasPoz, PlcTags.CpkBitisAci, PlcTags.CpkModGeriCekilmeAci,
                PlcTags.CpkTurSure, PlcTags.CpkTurSayi, PlcTags.CpkBomseGeriYon,
                null, null, null,
                PlcTags.CpkModHiz, PlcTags.CpkModBasPoz);

            Station(list, "Zımpara-1", PlcTags.Zim1BomseBasPoz, PlcTags.Zim1BitisAci, PlcTags.Zim1ModGeriCekilmeAci,
                PlcTags.Zim1TurSure, PlcTags.Zim1TurSayi, PlcTags.Zim1BomseYon,
                "Taşlama Hızı", PlcTags.Zim1TaslamaHiz, PlcTags.Zim1TaslamaYon,
                PlcTags.Zim1ModHiz, PlcTags.Zim1ModBasPoz);

            Station(list, "Zımpara-2", PlcTags.Zim2BomseBasPoz, PlcTags.Zim2BitisAci, PlcTags.Zim2ModGeriCekilmeAci,
                PlcTags.Zim2TurSure, PlcTags.Zim2TurSayi, PlcTags.Zim2BomseYon,
                "Taşlama Hızı", PlcTags.Zim2TaslamaHiz, PlcTags.Zim2TaslamaYon,
                PlcTags.Zim2ModHiz, PlcTags.Zim2ModBasPoz);

            Station(list, "Sünger-1", PlcTags.Sun1BomseBasPoz, PlcTags.Sun1BitisAci, PlcTags.Sun1ModGeriCekilmeAci,
                PlcTags.Sun1TurSure, PlcTags.Sun1TurSayi, PlcTags.Sun1BomseYon,
                "Sünger Hızı", PlcTags.Sun1SungerHiz, PlcTags.Sun1SungerYon,
                PlcTags.Sun1ModHiz, PlcTags.Sun1ModBasPoz);

            Station(list, "Sünger-2", PlcTags.Sun2BomseBasPoz, PlcTags.Sun2BitisAci, PlcTags.Sun2ModGeriCekilmeAci,
                PlcTags.Sun2TurSure, PlcTags.Sun2TurSayi, PlcTags.Sun2BomseYon,
                "Sünger Hızı", PlcTags.Sun2SungerHiz, PlcTags.Sun2SungerYon,
                PlcTags.Sun2ModHiz, PlcTags.Sun2ModBasPoz);

            Station(list, "Sünger-3", PlcTags.Sun3BomseBasPoz, PlcTags.Sun3BitisAci, PlcTags.Sun3ModGeriCekilmeAci,
                PlcTags.Sun3TurSure, PlcTags.Sun3TurSayi, PlcTags.Sun3BomseYon,
                "Sünger Hızı", PlcTags.Sun3SungerHiz, PlcTags.Sun3SungerYon,
                PlcTags.Sun3ModHiz, PlcTags.Sun3ModBasPoz);

            // Servo (form hız ayarları)
            Servo(list, "Servo — Çapak", PlcTags.CpkOranliMod, PlcTags.CpkKoselerHizli, PlcTags.CpkKoseHiz, PlcTags.CpkGeriMesafe);
            Servo(list, "Servo — Zımparalar", PlcTags.ZimOranliMod, PlcTags.ZimKoselerHizli, PlcTags.ZimKoseHiz, PlcTags.ZimGeriMesafe);
            Servo(list, "Servo — Süngerler", PlcTags.SunOranliMod, PlcTags.SunKoselerHizli, PlcTags.SunKoseHiz, PlcTags.SunGeriMesafe);

            // Transferler
            Transfer(list, "Transfer Yükleme", PlcTags.GtrAlmaPoz, PlcTags.GtrBirakmaPoz,
                PlcTags.GtrHizBos, PlcTags.GtrIvmeBos, PlcTags.GtrHizDolu, PlcTags.GtrIvmeDolu);
            Transfer(list, "Transfer Boşaltma", PlcTags.CtrAlmaPoz, PlcTags.CtrBirakmaPoz,
                PlcTags.CtrHizBos, PlcTags.CtrIvmeBos, PlcTags.CtrHizDolu, PlcTags.CtrIvmeDolu);

            // Çıkış
            list.Add(new ReceteParam("Çıkış Konveyör", "İstif Adet (Hedef)", PlcTags.IstifAdet, ReceteParamType.Int, "adet", 0, 0, 9999));
            list.Add(new ReceteParam("Çıkış Konveyör", "Konveyör Dönme Süresi", PlcTags.KonvDonmeSure, ReceteParamType.Real, "s", 1, 0, 9999));

            return list;
        }

        private static void Station(List<ReceteParam> list, string g,
            string basPoz, string bitisAci, string geriCekilme, string turSure, string turSayi, string bomseYon,
            string toolLabel, string toolHiz, string toolYon, string modHiz, string modBasPoz)
        {
            list.Add(new ReceteParam(g, "Bomse Başlama Pos", basPoz, ReceteParamType.Real, "°", 1, 0, 360));
            list.Add(new ReceteParam(g, "Bitiş Ek Açı", bitisAci, ReceteParamType.Real, "°", 1, 0, 360));
            list.Add(new ReceteParam(g, "Modül Geri Çekilme Açısı", geriCekilme, ReceteParamType.Real, "°", 1, 0, 360));
            list.Add(new ReceteParam(g, "Tur Süresi", turSure, ReceteParamType.Real, "s", 1, 0, 9999));
            list.Add(new ReceteParam(g, "Tur Sayısı", turSayi, ReceteParamType.Int, "adet", 0, 0, 9999));
            list.Add(new ReceteParam(g, "Bomse Geri Yön", bomseYon, ReceteParamType.Bool));

            if (toolHiz != null)
            {
                list.Add(new ReceteParam(g, toolLabel, toolHiz, ReceteParamType.Int, "%", 0, 0, 99));
                list.Add(new ReceteParam(g, toolLabel.Replace("Hızı", "Geri Yön"), toolYon, ReceteParamType.Bool));
            }

            list.Add(new ReceteParam(g, "Modül Hızı", modHiz, ReceteParamType.Int, "%", 0, 0, 9999));
            list.Add(new ReceteParam(g, "Modül Başlama Pozisyonu", modBasPoz, ReceteParamType.Real, "mm", 2, 0, 9999));
        }

        private static void Servo(List<ReceteParam> list, string g, string oranli, string koseler, string koseHiz, string geriMesafe)
        {
            list.Add(new ReceteParam(g, "Oranlı Mod", oranli, ReceteParamType.Bool));
            list.Add(new ReceteParam(g, "Köşeler Hızlı", koseler, ReceteParamType.Bool));
            list.Add(new ReceteParam(g, "Köşe Hızı", koseHiz, ReceteParamType.Real, "%", 0, 1, 100));
            list.Add(new ReceteParam(g, "Geri Mesafe", geriMesafe, ReceteParamType.Real, "mm", 1, 0, 100));
        }

        private static void Transfer(List<ReceteParam> list, string g, string alma, string birakma,
            string hizBos, string ivmeBos, string hizDolu, string ivmeDolu)
        {
            list.Add(new ReceteParam(g, "Alma Poz", alma, ReceteParamType.Real, "mm", 2, 0, 9999));
            list.Add(new ReceteParam(g, "Bırakma Poz", birakma, ReceteParamType.Real, "mm", 2, 0, 9999));
            list.Add(new ReceteParam(g, "Hız Boş", hizBos, ReceteParamType.Int, "%", 0, 0, 9999));
            list.Add(new ReceteParam(g, "İvme Boş", ivmeBos, ReceteParamType.Int, "%", 0, 0, 9999));
            list.Add(new ReceteParam(g, "Hız Dolu", hizDolu, ReceteParamType.Int, "%", 0, 0, 9999));
            list.Add(new ReceteParam(g, "İvme Dolu", ivmeDolu, ReceteParamType.Int, "%", 0, 0, 9999));
        }
    }
}
