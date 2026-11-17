using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EdgePanel.Core;
using Microsoft.Win32;

namespace EdgePanel;

public partial class SettingsWindow : Window
{
    static SettingsWindow? _open;
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    Place? _pickedPlace;
    int _monitor;
    string _language = "";

    public static void ShowSingle()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new SettingsWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    SettingsWindow()
    {
        InitializeComponent();
        Native.CenterOnPrimary(this);
        SourceInitialized += (_, _) =>
        {
            int dark = 1;   // DWMWA_USE_IMMERSIVE_DARK_MODE: dark title bar
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        // which build is running (helps tell copies apart)
        try { Title += $"  ·  {L.Version} {File.GetLastWriteTime(Environment.ProcessPath!):dd.MM.yyyy HH:mm}"; } catch { }
        var s = AppSettings.Current;

        // language: "" = follow Windows
        _language = s.Language is "tr" or "en" ? s.Language : "";
        foreach (var (code, label) in new[] { ("", L.LangAuto), ("tr", "Türkçe"), ("en", "English") })
            AddChoice(LanguageList, "lang", label, code == _language, () => _language = code);

        // monitors
        _monitor = s.MonitorIndex;
        AddMonitorOption(-1, L.MonitorAuto);
        foreach (var m in Native.GetMonitors()) AddMonitorOption(m.Index, m.ToString());

        NoActivateToggle.IsChecked = s.NoActivate;
        StartupToggle.IsChecked = IsStartupEnabled();

        CurrentCity.Text = L.CurrentCity(s.City);

        ClientIdBox.Text = s.DiscordClientId;
        SecretHint.Visibility = s.DiscordSecret.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDiscordStatus();
        DiscordService.Instance.Changed += UpdateDiscordStatus;
        Closed += (_, _) => DiscordService.Instance.Changed -= UpdateDiscordStatus;

        foreach (var code in ScoreService.LeagueNames.Keys)
        {
            var t = new ToggleButton
            {
                Style = (Style)FindResource("PillToggle"), Content = L.League(code), Tag = code,
                IsChecked = s.Leagues.Contains(code), Margin = new Thickness(0, 0, 6, 6),
            };
            LeagueList.Children.Add(t);
        }
    }

    void AddMonitorOption(int index, string text) =>
        AddChoice(MonitorList, "mon", text, index == _monitor, () => _monitor = index);

    void AddChoice(Panel host, string group, string text, bool isChecked, Action onChecked)
    {
        var rb = new RadioButton
        {
            Content = text, GroupName = group, Style = (Style)FindResource("SegmentButton"),
            IsChecked = isChecked, Margin = new Thickness(0, 0, 6, 6),
        };
        rb.Checked += (_, _) => onChecked();
        host.Children.Add(new Border { Style = (Style)FindResource("SegmentHost"), Child = rb, Margin = new Thickness(0, 0, 6, 6) });
    }

    void UpdateDiscordStatus()
    {
        var svc = DiscordService.Instance;
        DiscordStatus.Text = svc.State switch
        {
            DiscordState.Ready => L.StConnected(svc.Username),
            DiscordState.Authorizing => L.StAuthorizing,
            DiscordState.Connecting => L.Connecting,
            DiscordState.Offline => L.StOffline,
            DiscordState.NeedAuth => L.StNeedAuth,
            DiscordState.Error => svc.Message,
            _ => L.StNotSetUp,
        };
        DiscordStatus.ToolTip = svc.Message;
    }

    // ── weather ──────────────────────────────────────────────────

    void CityBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CitySearch_Click(sender, e);
    }

    async void CitySearch_Click(object sender, RoutedEventArgs e)
    {
        CityResults.Children.Clear();
        if (CityBox.Text.Trim().Length < 2) return;
        try
        {
            var places = await WeatherService.SearchAsync(CityBox.Text);
            if (places.Count == 0)
                CityResults.Children.Add(new TextBlock { Text = L.NoResults, Foreground = (System.Windows.Media.Brush)FindResource("Text2") });
            foreach (var p in places)
            {
                var b = new Button
                {
                    Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 6, 6),
                    Content = p.Detail.Length > 0 ? $"{p.Name} — {p.Detail}" : p.Name,
                };
                b.Click += (_, _) =>
                {
                    _pickedPlace = p;
                    CurrentCity.Text = L.PickedCity(p.Name, p.Detail);
                    CityResults.Children.Clear();
                };
                CityResults.Children.Add(b);
            }
        }
        catch (Exception ex)
        {
            CityResults.Children.Add(new TextBlock { Text = L.SearchFailed + ex.Message, Foreground = (System.Windows.Media.Brush)FindResource("Danger") });
        }
    }

    // ── save ─────────────────────────────────────────────────────

    bool Apply(bool notify = true)
    {
        var s = AppSettings.Current;
        s.Language = _language;
        s.MonitorIndex = _monitor;
        s.NoActivate = NoActivateToggle.IsChecked == true;
        if (_pickedPlace != null)
        {
            s.City = _pickedPlace.Name;
            s.Latitude = _pickedPlace.Latitude;
            s.Longitude = _pickedPlace.Longitude;
        }

        var newId = ClientIdBox.Text.Trim();
        bool discordChanged = newId != s.DiscordClientId || SecretBox.Password.Length > 0;
        if (newId != s.DiscordClientId) s.DiscordToken = "";   // token belongs to the old app
        s.DiscordClientId = newId;
        if (SecretBox.Password.Length > 0) s.DiscordSecret = AppSettings.Protect(SecretBox.Password.Trim());

        var leagues = LeagueList.Children.OfType<ToggleButton>().Where(t => t.IsChecked == true).Select(t => (string)t.Tag).ToList();
        if (leagues.Count > 0) s.Leagues = leagues;

        SetStartup(StartupToggle.IsChecked == true);
        AppSettings.Save();
        if (notify) AppSettings.NotifyChanged();
        return discordChanged;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        string before = L.Lang;
        Apply();
        Close();
        L.Init(AppSettings.Current.Language);
        bool changed = L.Lang != before;
        L.Init(before);                 // keep the running UI consistent until it restarts
        if (changed) App.Restart();     // every text is read once at start-up, so a new language needs a fresh start
    }
    void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    async void DiscordConnect_Click(object sender, RoutedEventArgs e)
    {
        Apply(notify: false);
        SecretBox.Password = "";
        SecretHint.Visibility = AppSettings.Current.DiscordSecret.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        await DiscordService.Instance.StartAsync(true);
        AppSettings.NotifyChanged();
    }

    void DiscordLogout_Click(object sender, RoutedEventArgs e) => DiscordService.Instance.Logout();

    void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // ── autostart (HKCU Run) ─────────────────────────────────────

    static bool IsStartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(AppInfo.Name) is string;
    }

    /// <summary>Moves an autostart entry from the old app name and keeps it pointing at the running exe.</summary>
    public static void MigrateAutostart()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            bool wanted = k.GetValue(AppInfo.Name) != null || k.GetValue(AppInfo.LegacyName) != null;
            if (k.GetValue(AppInfo.LegacyName) != null) k.DeleteValue(AppInfo.LegacyName);
            if (wanted) k.SetValue(AppInfo.Name, $"\"{Environment.ProcessPath}\"");
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on) k.SetValue(AppInfo.Name, $"\"{Environment.ProcessPath}\"");
            else if (k.GetValue(AppInfo.Name) != null) k.DeleteValue(AppInfo.Name);
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
