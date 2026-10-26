using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace IzostatikTabakKenarSilme.Services.Tabak
{
    /// <summary>
    /// Tabak konturundan 360 derecelik radyal profil.
    ///
    /// 1. Kontur: Excel noktaları sırayla çizgi parçaları; son nokta ilk
    ///    noktaya (tolerans içinde) dönmelidir.
    /// 2. Dönme merkezi: sınırlayıcı dikdörtgenin ortası
    ///    Xc = (Xmax+Xmin)/2, Yc = (Ymax+Ymin)/2. Merkez kontur içinde olmalı.
    /// 3. Her derece için (0°..359°, θ: X ekseninden Y'ye, saat yönü tersi)
    ///    merkezden X(t)=Xc+t·cosθ, Y(t)=Yc+t·sinθ ışını tüm parçalarla
    ///    kesiştirilir. Kesişim tek olmalıdır (radyal takip edilebilir
    ///    kontur); hiç yoksa ya da ışın konturu birden fazla kez kesiyorsa o
    ///    açı hatalı sayılır, sessizce seçim yapılmaz.
    /// 4. Sonuç: R[θ] (mm). Bir açı bile hatalıysa profil geçersizdir.
    /// </summary>
    public sealed class TabakProfile
    {
        public const int AngleCount = 360;

        // Kontur kapanma toleransı (mm) ve aynı kesişim sayılacak mesafe.
        private const double CloseTolerance = 0.5;
        private const double HitTolerance = 0.01;

        public IReadOnlyList<Point> Contour { get; private set; }
        public Point Center { get; private set; }
        public Rect Bounds { get; private set; }

        // R[k]: k° açısında merkezden kenara mesafe (mm); hatalı açıda NaN.
        public double[] Radii { get; } = new double[AngleCount];

        public double RMin { get; private set; } = double.NaN;
        public double RMax { get; private set; } = double.NaN;
        public int RMinAngle { get; private set; }
        public int RMaxAngle { get; private set; }

        // Genel hatalar (kontur, merkez) ve açı bazlı hatalar.
        public List<string> Errors { get; } = new List<string>();
        public List<int> BadAngles { get; } = new List<int>();

        public bool IsValid => Errors.Count == 0 && BadAngles.Count == 0;

        private TabakProfile()
        {
        }

        public static TabakProfile Compute(IList<Point> points)
        {
            var p = new TabakProfile();

            for (int k = 0; k < AngleCount; k++)
                p.Radii[k] = double.NaN;

            if (points == null || points.Count < 3)
            {
                p.Contour = points?.ToList() ?? new List<Point>();
                p.Errors.Add("Konturda en az 3 nokta olmalı.");
                return p;
            }

            // Kapanış noktası (ilkini tekrar eden son nokta) çıkarılır;
            // kapanmıyorsa kontur açık sayılır.
            var poly = points.ToList();
            double gap = (poly[poly.Count - 1] - poly[0]).Length;

            if (gap <= CloseTolerance)
                poly.RemoveAt(poly.Count - 1);
            else
                p.Errors.Add($"Kontur kapalı değil: son nokta ilk noktaya {gap:0.###} mm uzakta.");

            p.Contour = poly;

            double xmin = poly.Min(q => q.X), xmax = poly.Max(q => q.X);
            double ymin = poly.Min(q => q.Y), ymax = poly.Max(q => q.Y);
            p.Bounds = new Rect(xmin, ymin, xmax - xmin, ymax - ymin);
            p.Center = new Point((xmax + xmin) / 2, (ymax + ymin) / 2);

            if (p.Bounds.Width <= 0 || p.Bounds.Height <= 0)
            {
                p.Errors.Add("Kontur alanı sıfır.");
                return p;
            }

            if (!Contains(poly, p.Center))
            {
                p.Errors.Add("Dönme merkezi konturun dışında kalıyor.");
                return p;
            }

            for (int k = 0; k < AngleCount; k++)
            {
                double th = k * Math.PI / 180.0;
                List<double> hits = Intersections(poly, p.Center, Math.Cos(th), Math.Sin(th));

                if (hits.Count == 1)
                    p.Radii[k] = hits[0];
                else
                    p.BadAngles.Add(k);
            }

            if (p.BadAngles.Count > 0)
            {
                p.Errors.Add($"{p.BadAngles.Count} açıda kenar tek noktada kesilmiyor " +
                             $"(ör. {string.Join(", ", p.BadAngles.Take(6).Select(a => a + "°"))}); " +
                             "kontur merkezden radyal olarak takip edilemez.");
                return p;
            }

            p.RMin = p.Radii.Min();
            p.RMax = p.Radii.Max();
            p.RMinAngle = Array.IndexOf(p.Radii, p.RMin);
            p.RMaxAngle = Array.IndexOf(p.Radii, p.RMax);

            return p;
        }

        // Işının (merkez + t·d, t > 0) kontur parçalarıyla kesiştiği
        // mesafeler; köşeden geçen ışının iki parçada verdiği aynı nokta
        // tek sayılır.
        private static List<double> Intersections(List<Point> poly, Point c, double dx, double dy)
        {
            var ts = new List<double>();

            for (int i = 0; i < poly.Count; i++)
            {
                Point a = poly[i];
                Point b = poly[(i + 1) % poly.Count];

                double ex = b.X - a.X, ey = b.Y - a.Y;
                double den = dx * ey - dy * ex;

                if (Math.Abs(den) < 1e-12)
                    continue; // ışına paralel parça

                double ax = a.X - c.X, ay = a.Y - c.Y;
                double t = (ax * ey - ay * ex) / den;   // ışın üzerindeki mesafe
                double u = (ax * dy - ay * dx) / den;   // parça üzerindeki oran

                if (t > 1e-9 && u >= -1e-9 && u <= 1 + 1e-9)
                    ts.Add(t);
            }

            ts.Sort();

            var distinct = new List<double>();
            foreach (double t in ts)
            {
                if (distinct.Count == 0 || t - distinct[distinct.Count - 1] > HitTolerance)
                    distinct.Add(t);
            }

            return distinct;
        }

        // Çift-tek kuralı ile nokta konturun içinde mi?
        private static bool Contains(List<Point> poly, Point q)
        {
            bool inside = false;

            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Point a = poly[i], b = poly[j];

                if ((a.Y > q.Y) != (b.Y > q.Y) &&
                    q.X < (b.X - a.X) * (q.Y - a.Y) / (b.Y - a.Y) + a.X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
