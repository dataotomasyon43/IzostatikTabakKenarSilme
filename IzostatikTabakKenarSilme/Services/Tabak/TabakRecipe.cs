using System;
using System.Collections.Generic;
using System.Linq;

namespace IzostatikTabakKenarSilme.Services.Tabak
{
    /// <summary>
    /// 360 değerlik profilin PLC'nin Rec1..Rec6 dizilerine dönüşümü ve
    /// modül mekanik sınır kontrolü.
    ///
    /// PLC (ReceteTabakForm): FOR i := 0 TO 358
    ///   CamNode[i].Phase := i+1;  Distance := (Rec[i] - minson) / 10000
    /// Cam tablosu 0°'de mesafe 0 ile başlar. Buna göre:
    ///   dizi indeksi i (0..359) ↔ açı (i+1)°:
    ///   Rec1[0] = 1°, Rec1[59] = 60°, Rec2[0] = 61° ... Rec6[58] = 359°,
    ///   Rec6[59] = 360° (= 0° değeri; şimdiki PLC döngüsü kullanmıyor).
    ///   Değer = yarıçap (mm) × 10000. minson'ı PLC kendisi hesaplar.
    /// Doğrulama: SUNUM_13X18 için Omron cam grafiği (0..48 mm, 0°'de 0)
    /// bu ölçekle aynı.
    /// </summary>
    public static class TabakRecipe
    {
        public const double Scale = 10000.0;

        // İkinci eksen (modül) için kullanılabilir mutlak pozisyon aralığı.
        public const double MinPosMm = 400.0;
        public const double MaxPosMm = 600.0;

        /// <summary>i. dizi elemanının karşılık geldiği açı (derece, 0..359).</summary>
        public static int AngleOfIndex(int index)
        {
            return (index + 1) % TabakProfile.AngleCount;
        }

        public static float[][] ToRecArrays(TabakProfile profile)
        {
            if (profile == null || !profile.IsValid)
                throw new InvalidOperationException("Geçerli bir profil yok.");

            int arrays = PlcTags.RecArrays.Length;
            int len = PlcTags.RecLength;

            if (arrays * len != TabakProfile.AngleCount)
                throw new InvalidOperationException("Rec dizileri 360 değere uymuyor.");

            var rec = new float[arrays][];

            for (int a = 0; a < arrays; a++)
            {
                rec[a] = new float[len];

                for (int j = 0; j < len; j++)
                {
                    int index = a * len + j;
                    double r = profile.Radii[AngleOfIndex(index)];

                    if (double.IsNaN(r) || r <= 0)
                        throw new InvalidOperationException($"{AngleOfIndex(index)}° değeri geçersiz.");

                    rec[a][j] = (float)(r * Scale);
                }
            }

            return rec;
        }

        // ---------------- Mekanik sınır kontrolü ----------------

        /// <summary>Profili cam ile takip eden bir modül ekseni.</summary>
        public sealed class Module
        {
            public Module(string name, string startPosTag)
            {
                Name = name;
                StartPosTag = startPosTag;
            }

            public string Name { get; }

            // Modülün başlama pozisyonu (TEACH, REAL mm). Cam göreceli
            // başlar: pozisyon = başlama + (R - Rmin).
            public string StartPosTag { get; }
        }

        public static readonly IReadOnlyList<Module> Modules = new[]
        {
            new Module("Çapak Kırma", PlcTags.CpkModBasPoz),
            new Module("Zımpara-1", PlcTags.Zim1ModBasPoz),
            new Module("Zımpara-2", PlcTags.Zim2ModBasPoz),
            new Module("Sünger-1", PlcTags.Sun1ModBasPoz),
            new Module("Sünger-2", PlcTags.Sun2ModBasPoz),
            new Module("Sünger-3", PlcTags.Sun3ModBasPoz)
        };

        public sealed class ModuleCheck
        {
            public string Name;
            public double StartPos;
            public double MinPos;
            public double MaxPos;

            // Sınır dışı açılar ve o açıdaki pozisyon.
            public List<KeyValuePair<int, double>> Violations = new List<KeyValuePair<int, double>>();

            public bool Ok => Violations.Count == 0;

            public string ViolationText(int max = 5)
            {
                if (Ok)
                    return string.Empty;

                string list = string.Join(", ", Violations.Take(max)
                    .Select(v => $"{v.Key}° = {v.Value:0.00} mm"));

                return Violations.Count > max ? $"{list} … (+{Violations.Count - max})" : list;
            }
        }

        /// <summary>
        /// Her modül için 360 açının tamamında başlama + (R - Rmin) değerinin
        /// [400, 600] mm içinde kaldığını kontrol eder. Değerler kırpılmaz.
        /// </summary>
        public static ModuleCheck Check(TabakProfile profile, string name, double startPos)
        {
            var c = new ModuleCheck { Name = name, StartPos = startPos };

            double min = double.MaxValue, max = double.MinValue;

            for (int k = 0; k < TabakProfile.AngleCount; k++)
            {
                double pos = startPos + (profile.Radii[k] - profile.RMin);

                min = Math.Min(min, pos);
                max = Math.Max(max, pos);

                if (pos < MinPosMm || pos > MaxPosMm)
                    c.Violations.Add(new KeyValuePair<int, double>(k, pos));
            }

            c.MinPos = min;
            c.MaxPos = max;
            return c;
        }
    }
}
