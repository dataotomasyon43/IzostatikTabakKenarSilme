using System;
using System.Threading;
using System.Threading.Tasks;

namespace IzostatikTabakKenarSilme.Services
{
    // PLC yazmalarını arka planda, GELİŞ SIRASIYLA çalıştırır.
    //
    // Neden gerekli: Reset butonunda "bas → TRUE" ve "bırak → FALSE"
    // yazmaları ayrı Task.Run çağrılarıyla yapılırsa sıraları garanti
    // değildir; FALSE önce, TRUE sonra giderse HMI_Reset TRUE'da kalır.
    // Bu kuyruk her yazmayı bir öncekinin bitmesinden sonra başlatır.
    public sealed class PlcWriteQueue
    {
        private readonly object _gate = new object();
        private Task _tail = Task.CompletedTask;

        // Yazmayı kuyruğa ekler. Dönen Task beklenip hata yakalanmalıdır.
        // Bir yazmanın hata vermesi sonrakilerin çalışmasını engellemez.
        public Task Enqueue(Action write)
        {
            if (write == null)
                throw new ArgumentNullException(nameof(write));

            lock (_gate)
            {
                Task next = _tail.ContinueWith(
                    _ => write(),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);

                _tail = next;
                return next;
            }
        }

        // Kuyruktaki yazmaların bitmesini en fazla verilen süre kadar bekler.
        // Uygulama kapanırken, örneğin reset'in FALSE yazması PLC servisi
        // kapatılmadan önce gidebilsin diye kullanılır.
        public void WaitForIdle(TimeSpan timeout)
        {
            Task tail;

            lock (_gate)
                tail = _tail;

            try
            {
                tail.Wait(timeout);
            }
            catch
            {
                // Hata zaten yazmayı kuyruğa ekleyen tarafta ele alınır.
            }
        }
    }
}
