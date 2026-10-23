
using IzostatikTabakKenarSilme.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace IzostatikTabakKenarSilme.Pages
{
    public partial class CanliIzlemePage : Page
    {
        // Ana pencereye ait paylaşılan servis. Bu sayfa servisi
        // oluşturmaz ve dispose etmez.
        private readonly PlcService _plc;

        private readonly ObservableCollection<TagResult> _tags =
            new ObservableCollection<TagResult>();

        public CanliIzlemePage(PlcService plc)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));

            InitializeComponent();

            TagGrid.ItemsSource = _tags;

            AddTag("HMI_GTR_Akt_Poz", "REAL");
            AddTag("Mode", "INT");

            AddTag("Manuel", "BOOL");
            AddTag("Otomatik", "BOOL");
            AddTag("AutoCycle", "BOOL");
            AddTag("AlmOut", "BOOL");
            AddTag("HMI_AlmLamp", "BOOL");

            AddTag("GTRAlm", "BOOL");
            AddTag("CTRAlm", "BOOL");
            AddTag("Zim1Alm", "BOOL");
            AddTag("Zim2Alm", "BOOL");
            AddTag("Sun1Alm", "BOOL");
            AddTag("Sun2Alm", "BOOL");
            AddTag("Sun3Alm", "BOOL");

            for (int i = 1; i <= 8; i++)
                AddTag($"Bom{i}Alm", "BOOL");

            AddTag("I_Acil_Saha1", "BOOL");
            AddTag("I_Acil_Saha2", "BOOL");
            AddTag("I_Acil_Saha3", "BOOL");
            AddTag("I_Faz_OK", "BOOL");
            AddTag("I_GuvRole_OK", "BOOL");
            AddTag("I_Pano_Acil_Basili", "BOOL");

            AddTag("HMI_Konvde_Tabak_Var", "BOOL");
            AddTag("HMI_Recete_Yukle", "BOOL");
            AddTag("HMI_Recete_Yuklendi", "BOOL");

            AddTag("Alm", "BOOL[501]");
        }

        private void AddTag(string name, string type)
        {
            _tags.Add(new TagResult
            {
                Name = name,
                DataType = type,
                Value = "-",
                Status = "Test bekliyor"
            });
        }

        // async void yalnızca olay işleyicisi olduğu için kullanılır; tüm
        // istisnalar içeride yakalanır. PLC okumaları Task.Run ile arka
        // planda yapılır, await sonrası kod UI thread'inde devam eder.
        private async void ReadAll_Click(object sender, RoutedEventArgs e)
        {
            ReadAllButton.IsEnabled = false;

            try
            {
                try
                {
                    // Connect servis kilidini alır; ana penceredeki periyodik
                    // okuma sürüyorsa UI thread'i beklemesin diye arka planda.
                    await Task.Run(() => _plc.Connect());
                    StatusText.Text = "PLC erişimi başlatıldı; tag okumaları başlıyor.";
                }
                catch (Exception ex)
                {
                    StatusText.Text = "PLC bağlantı hatası: " + ex.Message;

                    foreach (var tag in _tags)
                        tag.Status = "Bağlantı kurulamadı";

                    TagGrid.Items.Refresh();
                    return;
                }

                int success = 0;
                int failed = 0;
                int index = 0;

                foreach (var tag in _tags)
                {
                    index++;
                    StatusText.Text = $"Okunuyor ({index}/{_tags.Count}): {tag.Name}";

                    try
                    {
                        string value = await Task.Run(() => ReadTagValue(tag));

                        if (value == null)
                        {
                            tag.Value = "-";
                            tag.Status = "Desteklenen okuma metodu yok";
                            failed++;
                        }
                        else
                        {
                            tag.Value = value;
                            tag.Status = "OK";
                            success++;
                        }
                    }
                    catch (Exception ex)
                    {
                        tag.Value = "-";
                        tag.Status = ex.Message;
                        failed++;
                    }

                    TagGrid.Items.Refresh();
                }

                StatusText.Text =
                    $"Test tamamlandı. Başarılı: {success} | Hatalı: {failed}";
            }
            finally
            {
                ReadAllButton.IsEnabled = true;
            }
        }

        // Arka plan thread'inde çalışır: UI kontrollerine ve TagResult
        // nesnesinin değerlerine yazmaz, yalnızca okunan değeri döndürür.
        // Desteklenmeyen tip için null döner.
        private string ReadTagValue(TagResult tag)
        {
            if (tag.DataType == "REAL")
            {
                float value = _plc.TestReadReal(tag.Name);
                return value.ToString();
            }

            if (tag.DataType == "INT")
            {
                short value = _plc.ReadIntTag(tag.Name);
                return value.ToString();
            }

            if (tag.DataType == "BOOL")
            {
                bool value = _plc.ReadBoolTag(tag.Name);
                return value.ToString();
            }

            if (tag.DataType.StartsWith("BOOL["))
            {
                int count = int.Parse(tag.DataType.Substring(5).TrimEnd(']'));
                bool[] alarms = _plc.ReadAlarmArray(tag.Name, count);

                var active = new List<int>();
                for (int i = 0; i < alarms.Length; i++)
                    if (alarms[i]) active.Add(i);

                return active.Count == 0
                    ? $"{alarms.Length} eleman; aktif yok"
                    : $"{alarms.Length} eleman; {active.Count} aktif (idx: {string.Join(",", active)})";
            }

            return null;
        }
    }

    public class TagResult
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public string Value { get; set; }
        public string Status { get; set; }
    }
}
