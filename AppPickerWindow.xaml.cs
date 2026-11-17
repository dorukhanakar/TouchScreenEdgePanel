using System.Windows;
using System.Windows.Controls;
using EdgePanel.Core;

namespace EdgePanel;

/// <summary>
/// Searchable list of the Start menu's apps (incl. Store/MSIX apps like Claude, which have no stable exe path).
/// A normal, activatable window so the search box gets keyboard input (the panel itself never takes focus).
/// </summary>
public partial class AppPickerWindow : Window
{
    static AppPickerWindow? _open;

    readonly Action<string, string> _add;
    readonly Func<string, bool> _exists;
    readonly Action _browse;
    List<StartMenuApp> _all = new();

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ShowSingle(Action<string, string> add, Func<string, bool> exists, Action browse)
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new AppPickerWindow(add, exists, browse);
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    AppPickerWindow(Action<string, string> add, Func<string, bool> exists, Action browse)
    {
        _add = add; _exists = exists; _browse = browse;
        InitializeComponent();
        Native.CenterOnPrimary(this);
        SourceInitialized += (_, _) =>
        {
            int dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        Loaded += (_, _) =>
        {
            _all = StartMenuApps.Load();
            foreach (var a in _all) a.Added = _exists(a.Path);
            ApplyFilter();
            SearchBox.Focus();
        };
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    void ApplyFilter()
    {
        var q = SearchBox.Text.Trim();
        var items = q.Length == 0 ? _all : _all.Where(a => L.Culture.CompareInfo.IndexOf(a.Name, q, System.Globalization.CompareOptions.IgnoreCase) >= 0).ToList();
        List.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 && _all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Item_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not StartMenuApp a || a.Added) return;
        _add(a.Name, a.Path);
        a.Added = true;
    }

    void Browse_Click(object sender, RoutedEventArgs e) => _browse();
    void Done_Click(object sender, RoutedEventArgs e) => Close();
}
