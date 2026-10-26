using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace IzostatikTabakKenarSilme.Services.Recete
{
    /// <summary>Kaydedilmiş bir reçete.</summary>
    [DataContract]
    public sealed class ReceteData
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public string Created { get; set; }
        [DataMember(Order = 3)] public string Modified { get; set; }

        // Tabak formu: adı ve reçete klasöründeki Excel kopyasının dosya adı
        // (yoksa null; reçete yalnız ayarları yükler).
        [DataMember(Order = 4)] public string FormName { get; set; }
        [DataMember(Order = 5)] public string FormFile { get; set; }

        // Tag → değer (BOOL 0/1).
        [DataMember(Order = 6)] public Dictionary<string, double> Values { get; set; } = new Dictionary<string, double>();

        // Kayıt sırasında dosya adı (JSON'a yazılmaz).
        public string FilePath { get; set; }

        public string ModifiedText => Modified ?? Created ?? string.Empty;
    }

    /// <summary>
    /// Reçeteler: uygulama dizinindeki Receteler klasöründe her reçete için
    /// &lt;ad&gt;.json ve tabak formunun kopyası &lt;ad&gt;.xlsx. Klasör
    /// kopyalanarak yedeklenir / taşınır. Etkin (son yüklenen) reçete
    /// aktif.txt'de tutulur.
    /// </summary>
    public sealed class ReceteStore
    {
        private const string ActiveFile = "aktif.txt";
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(ReceteData),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public string Folder { get; } =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Receteler");

        public static string Now()
        {
            return DateTime.Now.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }

        public List<ReceteData> List()
        {
            Directory.CreateDirectory(Folder);
            var list = new List<ReceteData>();

            foreach (string file in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    ReceteData r = Load(file);
                    if (r != null)
                        list.Add(r);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Reçete okunamadı ({Path.GetFileName(file)}): {ex.Message}");
                }
            }

            return list.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public ReceteData Load(string file)
        {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var r = (ReceteData)Serializer.ReadObject(fs);
                r.FilePath = file;
                if (r.Values == null)
                    r.Values = new Dictionary<string, double>();
                return r;
            }
        }

        public bool Exists(string name)
        {
            return File.Exists(JsonPath(name));
        }

        /// <summary>
        /// Reçeteyi kaydeder. formSourcePath verilirse Excel reçete
        /// klasörüne kopyalanır (form listeden silinse de reçete bozulmaz).
        /// </summary>
        public void Save(ReceteData r, string formSourcePath)
        {
            Directory.CreateDirectory(Folder);

            if (formSourcePath != null)
            {
                string formFile = SafeName(r.Name) + ".xlsx";
                string target = Path.Combine(Folder, formFile);

                if (!string.Equals(Path.GetFullPath(formSourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    using (var src = new FileStream(formSourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write))
                        src.CopyTo(dst);
                }

                r.FormFile = formFile;
            }

            string path = JsonPath(r.Name);
            string tmp = path + ".tmp";

            // Önce geçici dosyaya yaz: elektrik kesintisinde eski reçete bozulmasın.
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                Serializer.WriteObject(fs, r);

            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);

            r.FilePath = path;
            AppLogger.Info($"Reçete kaydedildi: {r.Name} ({r.Values.Count} ayar, form: {r.FormName ?? "yok"})");
        }

        public void Delete(ReceteData r)
        {
            File.Delete(JsonPath(r.Name));

            if (r.FormFile != null)
            {
                string form = Path.Combine(Folder, r.FormFile);
                if (File.Exists(form))
                    File.Delete(form);
            }

            AppLogger.Info($"Reçete silindi: {r.Name}");
        }

        public string FormPath(ReceteData r)
        {
            return r.FormFile == null ? null : Path.Combine(Folder, r.FormFile);
        }

        // ---------------- Etkin reçete ----------------

        public void SaveActive(string name)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, ActiveFile), name + "|" + Now(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                AppLogger.Error("Etkin reçete kaydedilemedi", ex);
            }
        }

        // (ad, zaman) ya da null
        public Tuple<string, string> LoadActive()
        {
            try
            {
                string path = Path.Combine(Folder, ActiveFile);
                if (!File.Exists(path))
                    return null;

                string[] parts = File.ReadAllText(path, Encoding.UTF8).Trim().Split('|');
                return parts.Length == 2 ? Tuple.Create(parts[0], parts[1]) : null;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Etkin reçete okunamadı: " + ex.Message);
                return null;
            }
        }

        // ---------------- Ad ----------------

        private string JsonPath(string name)
        {
            return Path.Combine(Folder, SafeName(name) + ".json");
        }

        // Dosya adında geçersiz karakterler '_' olur.
        public static string SafeName(string name)
        {
            var sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();

            foreach (char c in name.Trim())
                sb.Append(bad.Contains(c) ? '_' : c);

            return sb.ToString();
        }
    }
}
