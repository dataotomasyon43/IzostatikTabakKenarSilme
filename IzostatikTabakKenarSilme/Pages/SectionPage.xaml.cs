using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace IzostatikTabakKenarSilme.Pages
{
    // Bölüm sayfasındaki bir alt menü öğesi.
    public sealed class SectionItem
    {
        public SectionItem(string key, string title, Func<Page> create)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Create = create ?? throw new ArgumentNullException(nameof(create));
        }

        public string Key { get; }
        public string Title { get; }

        // Alt sayfa her seçimde yeniden oluşturulur.
        public Func<Page> Create { get; }
    }

    /// <summary>
    /// Sağda dikey alt menü olan bölüm sayfası (AYARLAR, ÜRETİM).
    /// </summary>
    public partial class SectionPage : Page
    {
        // Alt menünün bölündüğü en az satır sayısı (en kalabalık bölüm: AYARLAR, 12).
        public const int MinRows = 12;

        private readonly IList<SectionItem> _items;
        private readonly Action<string> _onSelected;
        private readonly Dictionary<string, Button> _buttons =
            new Dictionary<string, Button>();

        // initialKey: açılışta seçilecek alt sayfa (null/bilinmiyorsa ilki).
        // onSelected: seçim değişince çağrılır (ana pencere son seçimi hatırlar).
        public SectionPage(IList<SectionItem> items, string initialKey, Action<string> onSelected)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("En az bir alt menü öğesi gerekir.", nameof(items));

            _items = items;
            _onSelected = onSelected;

            InitializeComponent();

            // Uzun süre çalışan HMI: geçmiş sayfalar bellekte birikmesin.
            ContentFrame.Navigated += (s, e) =>
            {
                while (ContentFrame.CanGoBack)
                    ContentFrame.RemoveBackEntry();
            };

            // Satır sayısı en az MinRows: tüm bölümlerde buton boyu aynı kalır
            // ve 12 istasyonluk AYARLAR kaydırmasız sığar.
            SubMenuPanel.Rows = Math.Max(_items.Count, MinRows);

            var style = (Style)FindResource("SubMenuButtonStyle");

            foreach (SectionItem item in _items)
            {
                var button = new Button
                {
                    Content = item.Title,
                    CommandParameter = item.Key,
                    Style = style
                };

                button.Click += SubMenuButton_Click;
                SubMenuPanel.Children.Add(button);
                _buttons[item.Key] = button;
            }

            Select(initialKey);
        }

        private void SubMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is string key)
                Select(key);
        }

        public void Select(string key)
        {
            SectionItem item = _items.FirstOrDefault(i => i.Key == key) ?? _items[0];

            ContentFrame.Navigate(item.Create());

            foreach (KeyValuePair<string, Button> pair in _buttons)
                pair.Value.Tag = pair.Key == item.Key ? "Active" : null;

            _onSelected?.Invoke(item.Key);
        }
    }
}
