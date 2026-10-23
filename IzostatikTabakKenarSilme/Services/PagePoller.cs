using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace IzostatikTabakKenarSilme.Services
{
    // Sayfaya özel PLC okuma döngüsü. Sayfa ekrandayken (Loaded) çalışır,
    // ekrandan çıkınca (Unloaded) durur; böylece yalnızca açık sayfanın
    // tag'leri okunur ve haberleşme gereksiz yorulmaz.
    //
    // poll: bir okuma turu. Okumayı Task.Run içinde yapıp sonucu (await
    // sonrası, UI thread'inde) ekrana yazmalıdır. Hata fırlatırsa onError
    // çağrılır ve döngü devam eder.
    public sealed class PagePoller
    {
        private readonly FrameworkElement _owner;
        private readonly TimeSpan _interval;
        private readonly Func<CancellationToken, Task> _poll;
        private readonly Action<Exception> _onError;

        private CancellationTokenSource _cts;

        public PagePoller(FrameworkElement owner, TimeSpan interval,
            Func<CancellationToken, Task> poll, Action<Exception> onError = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _interval = interval;
            _poll = poll ?? throw new ArgumentNullException(nameof(poll));
            _onError = onError;

            _owner.Loaded += Owner_Loaded;
            _owner.Unloaded += Owner_Unloaded;
        }

        private void Owner_Loaded(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
                return;

            _cts = new CancellationTokenSource();
            _ = RunAsync(_cts.Token);
        }

        private void Owner_Unloaded(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = null;
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _poll(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Uygulama kapanıyor (PLC servisi kapatıldı).
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    try { _onError?.Invoke(ex); } catch { }
                }

                try
                {
                    await Task.Delay(_interval, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
