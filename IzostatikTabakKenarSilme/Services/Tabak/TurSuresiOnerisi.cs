using System;
using System.IO;
using System.Linq;

namespace IzostatikTabakKenarSilme.Services.Tabak
{
    /// <summary>
    /// PLC'ye en son yüklenen tabak formuna göre önerilen en kısa tur süresi
    /// (yalnız tavsiye; sınır değildir, operatör daha kısa yazabilir).
    ///
    /// Modül bomseye cam ile bağlı: bomse 360/T °/s dönerken modülün hızı
    /// v = (dR/dθ)·ω, ivmesi a = (d²R/dθ²)·ω². Modül sınırları aşılmasın:
    ///   T ≥ 360·max|dR/dθ| / V      ve      T ≥ 360·√(max|d²R/dθ²| / A)
    /// Türevler 1°'lik yarıçap dizisinden merkezi farkla. Oranlı mod (köşede
    /// yavaşlama) hesaba katılmaz; öneri güvenli taraftadır.
    /// </summary>
    public static class TurSuresiOnerisi
    {
        // Modülün cam takibinde zorlanmadan çıkabileceği hız / ivme (ilk
        // değerler; makinede denenerek ayarlanır).
        public const double ModulHizSiniri = 300.0;   // mm/s
        public const double ModulIvmeSiniri = 5000.0; // mm/s²

        public sealed class Sonuc
        {
            public string FormAdi { get; set; }
            public double OnerilenSn { get; set; }
        }

        private static readonly object Sync = new object();
        private static string _cacheKey;
        private static Sonuc _cache;

        /// <summary>PLC'deki form bilinmiyor / okunamıyorsa null. Arka planda çağrılmalı.</summary>
        public static Sonuc Hesapla(TabakLibrary library)
        {
            try
            {
                Tuple<string, DateTime> last = library.LoadLastLoaded();
                if (last == null)
                    return null;

                TabakLibrary.Entry entry = library.List()
                    .FirstOrDefault(e => string.Equals(e.Name, last.Item1, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    return null;

                string key = entry.Path + "|" + File.GetLastWriteTimeUtc(entry.Path).Ticks;

                lock (Sync)
                {
                    if (key == _cacheKey)
                        return _cache;
                }

                ExcelPointReader.Result read = ExcelPointReader.Read(entry.Path);
                TabakProfile profile = TabakProfile.Compute(read.Points);
                double t = EnKisaTur(profile.Radii);

                Sonuc sonuc = double.IsNaN(t) ? null : new Sonuc
                {
                    FormAdi = entry.Name,
                    OnerilenSn = Math.Ceiling(t * 2) / 2 // 0,5 s'ye yukarı yuvarla
                };

                lock (Sync)
                {
                    _cacheKey = key;
                    _cache = sonuc;
                }

                return sonuc;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Tur süresi önerisi hesaplanamadı: " + ex.Message);
                return null;
            }
        }

        // radii[k]: k. derecedeki yarıçap (mm). Hesaplanamayan açılar atlanır.
        public static double EnKisaTur(double[] radii)
        {
            int n = radii?.Length ?? 0;
            if (n < 3)
                return double.NaN;

            double maxD1 = 0, maxD2 = 0;

            for (int i = 0; i < n; i++)
            {
                double a = radii[(i - 1 + n) % n], b = radii[i], c = radii[(i + 1) % n];
                if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c))
                    continue;

                maxD1 = Math.Max(maxD1, Math.Abs(c - a) / 2); // mm/°
                maxD2 = Math.Max(maxD2, Math.Abs(c - 2 * b + a)); // mm/°²
            }

            double tHiz = 360.0 * maxD1 / ModulHizSiniri;
            double tIvme = 360.0 * Math.Sqrt(maxD2 / ModulIvmeSiniri);
            return Math.Max(tHiz, tIvme);
        }
    }
}
