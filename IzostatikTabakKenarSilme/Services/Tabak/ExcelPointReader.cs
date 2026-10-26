using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Windows;
using System.Xml.Linq;

namespace IzostatikTabakKenarSilme.Services.Tabak
{
    /// <summary>
    /// Tabak formu Excel dosyasını (.xlsx) okur.
    ///
    /// Doğrulanan format (SUNUM_13X18.xlsx): ilk çalışma sayfası, A sütunu X,
    /// B sütunu Y (mm), başlıksız, her satır bir kontur noktası; son satır
    /// ilk noktayı tekrarlayarak konturu kapatır.
    ///
    /// Ek paket kullanılmaz: .xlsx bir ZIP paketidir, WPF ile gelen
    /// System.IO.Packaging (WindowsBase) ile açılır. Eski .xls desteklenmez.
    /// A ve B'si sayı olmayan satırlar (ör. başlık) atlanır ve sayılır.
    /// </summary>
    public static class ExcelPointReader
    {
        private static readonly XNamespace Main =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRel =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        public sealed class Result
        {
            public List<Point> Points { get; } = new List<Point>();

            // Sayı olmayan ya da eksik olduğu için atlanan satır sayısı.
            public int SkippedRows { get; set; }
        }

        public static Result Read(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Yalnızca .xlsx dosyaları okunabilir.");

            // Excel'de açıkken de okunabilsin.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Package package = Package.Open(stream, FileMode.Open, FileAccess.Read))
            {
                Uri sheetUri = FirstSheetUri(package);
                XDocument sheet = Load(package, sheetUri);

                var result = new Result();

                foreach (XElement row in sheet.Descendants(Main + "row"))
                {
                    double? x = null, y = null;

                    foreach (XElement c in row.Elements(Main + "c"))
                    {
                        string col = ColumnOf((string)c.Attribute("r"));

                        if (col == "A")
                            x = NumberOf(c);
                        else if (col == "B")
                            y = NumberOf(c);
                    }

                    if (x.HasValue && y.HasValue)
                        result.Points.Add(new Point(x.Value, y.Value));
                    else
                        result.SkippedRows++;
                }

                return result;
            }
        }

        // workbook.xml → ilk <sheet> → ilişki → çalışma sayfası parçası.
        private static Uri FirstSheetUri(Package package)
        {
            var workbookUri = new Uri("/xl/workbook.xml", UriKind.Relative);
            XDocument workbook = Load(package, workbookUri);

            XElement firstSheet = workbook.Descendants(Main + "sheet").FirstOrDefault()
                ?? throw new InvalidDataException("Excel dosyasında çalışma sayfası yok.");

            string relId = (string)firstSheet.Attribute(Rel + "id");

            XDocument rels = Load(package, new Uri("/xl/_rels/workbook.xml.rels", UriKind.Relative));
            XElement rel = rels.Descendants(PkgRel + "Relationship")
                .FirstOrDefault(r => (string)r.Attribute("Id") == relId)
                ?? throw new InvalidDataException("Çalışma sayfası ilişkisi bulunamadı.");

            string target = (string)rel.Attribute("Target");

            // Hedef "worksheets/sheet1.xml" (xl/'ye göre) ya da "/xl/..." olabilir.
            string full = target.StartsWith("/") ? target : "/xl/" + target;
            return new Uri(full, UriKind.Relative);
        }

        private static XDocument Load(Package package, Uri uri)
        {
            if (!package.PartExists(uri))
                throw new InvalidDataException($"Excel dosyası beklenen yapıda değil ({uri}).");

            using (Stream s = package.GetPart(uri).GetStream(FileMode.Open, FileAccess.Read))
                return XDocument.Load(s);
        }

        // "AB12" → "AB"
        private static string ColumnOf(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef))
                return string.Empty;

            int i = 0;
            while (i < cellRef.Length && char.IsLetter(cellRef[i]))
                i++;

            return cellRef.Substring(0, i).ToUpperInvariant();
        }

        // Yalnızca sayı hücreleri (t yok ya da t="n"); metin, formül hatası
        // vb. null döner.
        private static double? NumberOf(XElement c)
        {
            string type = (string)c.Attribute("t");

            if (type != null && type != "n")
                return null;

            string v = (string)c.Element(Main + "v");

            if (string.IsNullOrWhiteSpace(v))
                return null;

            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                ? d
                : (double?)null;
        }
    }
}
