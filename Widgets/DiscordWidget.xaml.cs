using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using EdgePanel.Core;

namespace EdgePanel.Widgets;

public partial class DiscordWidget : UserControl
{
    static DiscordService Svc => DiscordService.Instance;
    readonly Dictionary<string, Ellipse> _rings = new();
    string? _guildId;

    public DiscordWidget()
    {
        InitializeComponent();
        Svc.Changed += Render;
        Svc.SpeakingChanged += (id, on) =>
        {
            if (_rings.TryGetValue(id, out var ring)) ring.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        };
        AppSettings.Changed += () =>
        {
            if (Svc.State is DiscordState.Unconfigured or DiscordState.NeedAuth or DiscordState.Error)
                _ = Svc.StartAsync(false);
        };
        Loaded += async (_, _) => { Render(); await Svc.StartAsync(false); };
    }

    Brush Res(string key) => (Brush)FindResource(key);

    // ── render ───────────────────────────────────────────────────

    void Render()
    {
        var st = Svc.State;
        bool ready = st == DiscordState.Ready;
        UserText.Text = ready ? Svc.Username : "";

        // status pill
        if (ready && Svc.ChannelId != null)
        {
            StatusText.Text = L.Upper(Svc.ChannelName ?? "");
            StatusDot.Fill = Res("Accent");
            StatusText.Foreground = Res("Accent");
            StatusPill.BorderBrush = Res("AccentDim");
        }
        else
        {
            StatusText.Text = st switch
            {
                DiscordState.Ready => L.PillNotInChannel,
                DiscordState.Offline => "DISCORD KAPALI",
                DiscordState.Connecting or DiscordState.Authorizing => L.PillConnecting,
                DiscordState.Error => "HATA",
                _ => L.PillNotConnected,
            };
            StatusDot.Fill = Res(st == DiscordState.Error ? "Danger" : "Text3");
            StatusText.Foreground = Res("Text2");
            StatusPill.BorderBrush = Res("CardBorder");
        }

        RenderOverlay(st);
        if (!ready) return;

        // action buttons
        SetDanger(BtnMute, Svc.Mute || Svc.Deaf);
        MuteIcon.Text = Svc.Mute || Svc.Deaf ? "" : "";
        MuteLabel.Text = Svc.Mute ? L.Unmute : L.Mute;
        SetDanger(BtnDeaf, Svc.Deaf);
        DeafLabel.Text = Svc.Deaf ? L.Undeafen : L.Deafen;
        bool ptt = Svc.Mode == "PUSH_TO_TALK";
        ModeIcon.Text = ptt ? "" : "";
        ModeLabel.Text = ptt ? L.PushToTalk : L.VoiceActivity;
        BtnLeave.IsEnabled = Svc.ChannelId != null;

        InVolText.Text = Svc.InputVolume.ToString();
        OutVolText.Text = Svc.OutputVolume.ToString();
        TglNS.IsChecked = Svc.NoiseSuppression;
        TglEC.IsChecked = Svc.EchoCancellation;
        TglAGC.IsChecked = Svc.AutoGain;
        TglQoS.IsChecked = Svc.Qos;

        RenderMembers();
    }

    void SetDanger(Button b, bool on)
    {
        b.BorderBrush = on ? Res("Danger") : Res("CardBorder");
        b.Background = on ? Res("DangerDim") : new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x18));
        b.Foreground = on ? Res("Danger") : Res("Text1");
    }

    void RenderOverlay(DiscordState st)
    {
        (string title, string text, string? button) = st switch
        {
            DiscordState.Unconfigured => (L.OvConnectTitle, L.OvConnectText, L.OpenSettings),
            DiscordState.NeedAuth when !DiscordService.IsDiscordRunning() => (L.OvClosedTitle, L.OvClosedNeedAuthText, L.OpenDiscord),
            DiscordState.NeedAuth => (L.OvNeedAuthTitle, L.OvNeedAuthText, L.Connect),
            DiscordState.Offline when Svc.Launching => (L.OvLaunchingTitle, L.OvLaunchingText, null),
            DiscordState.Offline => (L.OvClosedTitle, L.OvClosedText, L.OpenDiscord),
            DiscordState.Connecting => (L.Connecting, "", null),
            DiscordState.Authorizing => (L.OvAuthorizingTitle, Svc.Message, null),
            DiscordState.Error => (L.OvErrorTitle, Svc.Message, L.Retry),
            _ => ("", "", null),
        };
        Overlay.Visibility = st == DiscordState.Ready ? Visibility.Collapsed : Visibility.Visible;
        OverlayTitle.Text = title;
        OverlayText.Text = text;
        OverlayText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        OverlayButton.Visibility = button != null ? Visibility.Visible : Visibility.Collapsed;
        OverlayButton.Content = button;
    }

    void RenderMembers()
    {
        bool inChannel = Svc.ChannelId != null && Svc.Members.Count > 0;
        IdlePlaceholder.Visibility = inChannel ? Visibility.Collapsed : Visibility.Visible;
        MembersScroll.Visibility = inChannel ? Visibility.Visible : Visibility.Collapsed;
        MemberPanel.Children.Clear();
        _rings.Clear();
        if (!inChannel) return;

        foreach (var m in Svc.Members)
        {
            var avatar = new Ellipse { Width = 52, Height = 52, Fill = new SolidColorBrush(Color.FromRgb(0x2B, 0x2D, 0x31)) };
            _ = LoadAvatar(avatar, m.Avatar);
            var ring = new Ellipse
            {
                Width = 60, Height = 60, Stroke = Res("Accent"), StrokeThickness = 3,
                Visibility = m.Speaking ? Visibility.Visible : Visibility.Hidden,
            };
            _rings[m.Id] = ring;

            var face = new Grid { Width = 60, Height = 60, HorizontalAlignment = HorizontalAlignment.Center };
            face.Children.Add(avatar);
            face.Children.Add(ring);
            if (m.Mute || m.Deaf)
            {
                face.Children.Add(new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Res("Danger"),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Child = new TextBlock
                    {
                        Text = m.Deaf ? "" : "", Style = (Style)FindResource("Glyph"), FontSize = 11,
                        Foreground = Brushes.White,
                    },
                });
            }

            var item = new StackPanel { Width = 92, Margin = new Thickness(4, 2, 4, 8) };
            item.Children.Add(face);
            item.Children.Add(new TextBlock
            {
                Text = m.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = m.Deaf || m.Mute ? Res("Text2") : Res("Text1"),
            });
            MemberPanel.Children.Add(item);
        }
    }

    static async Task LoadAvatar(Ellipse e, string url)
    {
        var img = await ImageCache.GetAsync(url, 104);
        if (img != null) e.Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
    }

    // ── controls ─────────────────────────────────────────────────

    async void Mute_Click(object s, RoutedEventArgs e)
    {
        // when deafened, "unmute" in Discord also undeafens
        if (Svc.Deaf) await Svc.ToggleDeaf(); else await Svc.ToggleMute();
    }
    async void Deaf_Click(object s, RoutedEventArgs e) => await Svc.ToggleDeaf();
    async void Mode_Click(object s, RoutedEventArgs e) => await Svc.ToggleMode();
    async void Leave_Click(object s, RoutedEventArgs e) => await Svc.LeaveAsync();
    async void InDown_Click(object s, RoutedEventArgs e) => await Svc.SetInputVolume(Svc.InputVolume - 10);
    async void InUp_Click(object s, RoutedEventArgs e) => await Svc.SetInputVolume(Svc.InputVolume + 10);
    async void OutDown_Click(object s, RoutedEventArgs e) => await Svc.SetOutputVolume(Svc.OutputVolume - 10);
    async void OutUp_Click(object s, RoutedEventArgs e) => await Svc.SetOutputVolume(Svc.OutputVolume + 10);

    async void Flag_Click(object sender, RoutedEventArgs e)
    {
        var t = (ToggleButton)sender;
        await Svc.SetFlag((string)t.Tag, t.IsChecked == true);
        Render();   // snap back to Discord's real value if the call failed
    }

    async void Overlay_Click(object sender, RoutedEventArgs e)
    {
        if (Svc.State == DiscordState.Unconfigured) { SettingsWindow.ShowSingle(); return; }
        if (Svc.State == DiscordState.Offline ||
            (Svc.State == DiscordState.NeedAuth && !DiscordService.IsDiscordRunning())) { Svc.LaunchDiscord(); return; }
        await Svc.StartAsync(true);
    }

    // ── tabs / channel browser ───────────────────────────────────

    async void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (ControlsView == null || ChannelsView == null) return;   // during InitializeComponent
        bool channels = TabChannels.IsChecked == true;
        ControlsView.Visibility = channels ? Visibility.Collapsed : Visibility.Visible;
        ChannelsView.Visibility = channels ? Visibility.Visible : Visibility.Collapsed;
        if (channels) await LoadGuildsAsync();
    }

    async Task LoadGuildsAsync()
    {
        GuildList.Children.Clear();
        ChannelList.Children.Clear();
        if (Svc.State != DiscordState.Ready) { ShowChannelsMsg(L.ConnectFirst); return; }
        try
        {
            ShowChannelsMsg(L.LoadingServers);
            var guilds = await Svc.GetGuildsAsync();
            ShowChannelsMsg(guilds.Count == 0 ? L.NoServers : null);
            foreach (var g in guilds)
            {
                var b = ListButton(g.Name, g.Id == _guildId);
                b.Click += async (_, _) => { _guildId = g.Id; HighlightGuild(b); await LoadChannelsAsync(g.Id); };
                GuildList.Children.Add(b);
            }
            if (_guildId != null && guilds.Any(g => g.Id == _guildId)) await LoadChannelsAsync(_guildId);
        }
        catch (Exception ex) { ShowChannelsMsg(L.ServersFailed + ex.Message); }
    }

    async Task LoadChannelsAsync(string guildId)
    {
        ChannelList.Children.Clear();
        try
        {
            foreach (var c in await Svc.GetVoiceChannelsAsync(guildId))
            {
                var b = ListButton(c.Name, c.Id == Svc.ChannelId);
                b.Click += async (_, _) =>
                {
                    await Svc.JoinAsync(c.Id);
                    TabControls.IsChecked = true;
                };
                ChannelList.Children.Add(b);
            }
            if (ChannelList.Children.Count == 0)
                ChannelList.Children.Add(new TextBlock { Text = L.NoVoiceChannels, Foreground = Res("Text2"), Margin = new Thickness(6) });
        }
        catch (Exception ex) { App.Log(ex); }
    }

    Button ListButton(string text, bool active)
    {
        var b = new Button
        {
            Style = (Style)FindResource("FlatButton"),
            Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Height = 44, Margin = new Thickness(0, 0, 0, 6),
        };
        if (active) { b.BorderBrush = Res("Accent"); b.Foreground = Res("Accent"); }
        return b;
    }

    void HighlightGuild(Button active)
    {
        foreach (Button b in GuildList.Children.OfType<Button>())
        {
            b.BorderBrush = b == active ? Res("Accent") : Res("CardBorder");
            b.Foreground = b == active ? Res("Accent") : Res("Text1");
        }
    }

    void ShowChannelsMsg(string? msg)
    {
        ChannelsMsg.Text = msg ?? "";
        ChannelsMsg.Visibility = msg == null ? Visibility.Collapsed : Visibility.Visible;
    }
}
