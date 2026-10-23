using System;
using IzostatikTabakKenarSilme.Services;

namespace IzostatikTabakKenarSilme.Controls
{
    // PLC'ye bağlı basılı tutma butonu: basınca PlcTag = TRUE, bırakınca
    // (parmak kayınca, buton devre dışı kalınca, sayfa kapanınca da)
    // PlcTag = FALSE. Poz git, öğren gibi komutlar için.
    //
    // Commands sayfa tarafından verilir; yazma yalnızca operatör basınca
    // olur. Hata olursa CommandFailed olayı çıkar (sayfa gösterir).
    public class PlcPushButton : HoldButton
    {
        public PlcPushButton()
        {
            Pressed += async (s, e) => await RunAsync(true);
            Released += async (s, e) => await RunAsync(false);
        }

        // Yazılacak BOOL tag (PlcTags'ten).
        public string PlcTag { get; set; }

        // Log ve hata mesajlarında görünen ad.
        public string Description { get; set; }

        public PlcCommands Commands { get; set; }

        public event EventHandler<string> CommandFailed;

        private async System.Threading.Tasks.Task RunAsync(bool press)
        {
            if (Commands == null || string.IsNullOrEmpty(PlcTag))
                return;

            string text = $"{Description ?? PlcTag} {(press ? "bas" : "bırak")}";

            try
            {
                if (press)
                    await Commands.PressMomentaryAsync(PlcTag);
                else
                    await Commands.ReleaseMomentaryAsync(PlcTag);

                AppLogger.Info($"Manuel komut: {text}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Manuel komut gönderilemedi: {text}", ex);
                CommandFailed?.Invoke(this, $"Komut gönderilemedi ({text}): {ex.Message}");
            }
        }
    }
}
