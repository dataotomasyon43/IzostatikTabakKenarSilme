using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IzostatikTabakKenarSilme.Services
{
    // Tüm makine için gereken ve ana pencerenin düzenli okuduğu durum.
    // Sayfalar bu değerleri PLC'den tekrar OKUMAZ; buradan dinler.
    //
    // Yalnızca UI thread'inden güncellenir (ana pencerenin izleme döngüsü).
    // Sayfalar PropertyChanged'e abone olur ve Unloaded'da aboneliği
    // bırakmalıdır (bu nesne uygulama boyunca yaşar; bırakılmayan abonelik
    // sayfayı bellekte tutar).
    public sealed class MachineState : INotifyPropertyChanged
    {
        private bool _isConnected;
        private short? _mode;
        private bool? _autoCycle;
        private int _activeAlarmCount;

        public event PropertyChangedEventHandler PropertyChanged;

        // PLC ile haberleşme var mı (son Mode okuması başarılı mı).
        public bool IsConnected
        {
            get => _isConnected;
            set => Set(ref _isConnected, value);
        }

        // Mode tag'i (0 Kapalı, 1 Güvenli, 2 Manuel, 3 Otomatik); bilinmiyorsa null.
        public short? Mode
        {
            get => _mode;
            set => Set(ref _mode, value);
        }

        // Otomatik çevrim çalışıyor mu (AutoCycle); bilinmiyorsa null.
        public bool? AutoCycle
        {
            get => _autoCycle;
            set => Set(ref _autoCycle, value);
        }

        // Aktif alarm sayısı (Alm dizisi).
        public int ActiveAlarmCount
        {
            get => _activeAlarmCount;
            set => Set(ref _activeAlarmCount, value);
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
