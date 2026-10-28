using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace IzostatikTabakKenarSilme.Services.Tabak
{
    /// <summary>
    /// Tabak formu kütüphanesi: Excel (.xlsx) / .csv dosyaları uygulama dizinindeki
    /// TabakFormlari klasöründe saklanır (eklenen dosyanın kopyası).
    /// PLC'ye en son yüklenen form ve zamanı son_yuklenen.txt dosyasında
    /// tutulur.
    /// </summary>
    public sealed class TabakLibrary
    {
        private const string LastLoadedFile = "son_yuklenen.txt";

        public string Directory { get; } =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TabakFormlari");

        public sealed class Entry
        {
            public string Name { get; set; }       // uzantısız ad
            public string Path { get; set; }
            public DateTime Modified { get; set; }

            public string ModifiedText => Modified.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        public List<Entry> List()
        {
            System.IO.Directory.CreateDirectory(Directory);

            return new DirectoryInfo(Directory)
                .GetFiles()
                .Where(f => ExcelPointReader.IsSupported(f.Name))
                .Where(f => !f.Name.StartsWith("~$")) // Excel geçici dosyaları
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(f => new Entry
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(f.Name),
                    Path = f.FullName,
                    Modified = f.LastWriteTime
                })
                .ToList();
        }

        public string TargetPathFor(string sourcePath)
        {
            return System.IO.Path.Combine(Directory, System.IO.Path.GetFileName(sourcePath));
        }

        /// <summary>
        /// Dosya bizim tabak formu biçimimize uyuyor mu: okunabiliyor (.xlsx /
        /// .csv, A=X B=Y), kapalı ve geçerli bir kontur veriyor mu. Uygunsa
        /// null, değilse nedeni döner. Kütüphaneye yalnız uygun dosyalar
        /// eklenir.
        /// </summary>
        public static string CheckFormat(string path, out ExcelPointReader.Result read, out TabakProfile profile)
        {
            read = null;
            profile = null;

            try
            {
                read = ExcelPointReader.Read(path);
            }
            catch (Exception ex)
            {
                return ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException
                    ? ex.Message
                    : "Dosya okunamadı: " + ex.Message;
            }

            if (read.Points.Count == 0)
                return "Dosyada X/Y nokta yok (A sütunu X, B sütunu Y olmalı).";

            try
            {
                profile = TabakProfile.Compute(read.Points);
            }
            catch (Exception ex)
            {
                return "Kontur hesaplanamadı: " + ex.Message;
            }

            return profile.IsValid ? null : string.Join(" ", profile.Errors);
        }

        public static string CheckFormat(string path)
        {
            return CheckFormat(path, out _, out _);
        }

        /// <summary>Dosyayı kütüphaneye kopyalar (aynı ad varsa üzerine yazar).</summary>
        public string Import(string sourcePath)
        {
            System.IO.Directory.CreateDirectory(Directory);

            string target = TargetPathFor(sourcePath);

            // Kaynak Excel'de açıkken de kopyalanabilsin.
            using (var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write))
                src.CopyTo(dst);

            AppLogger.Info($"Tabak formu eklendi: {System.IO.Path.GetFileName(target)}");
            return target;
        }

        public void Delete(Entry entry)
        {
            File.Delete(entry.Path);
            AppLogger.Info($"Tabak formu silindi: {entry.Name}");
        }

        // ---------------- Son yüklenen ----------------

        public void SaveLastLoaded(string name, DateTime time)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(System.IO.Path.Combine(Directory, LastLoadedFile),
                    name + "|" + time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                AppLogger.Error("Son yüklenen form kaydedilemedi", ex);
            }
        }

        // (ad, zaman) ya da yoksa null
        public Tuple<string, DateTime> LoadLastLoaded()
        {
            try
            {
                string path = System.IO.Path.Combine(Directory, LastLoadedFile);

                if (!File.Exists(path))
                    return null;

                string[] parts = File.ReadAllText(path, Encoding.UTF8).Trim().Split('|');

                if (parts.Length == 2 &&
                    DateTime.TryParseExact(parts[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime t))
                {
                    return Tuple.Create(parts[0], t);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Son yüklenen form okunamadı: " + ex.Message);
            }

            return null;
        }
    }
}
