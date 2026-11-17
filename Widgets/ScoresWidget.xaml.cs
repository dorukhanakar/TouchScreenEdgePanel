using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EdgePanel.Core;

namespace EdgePanel.Widgets;

public partial class ScoresWidget : UserControl
{
    static readonly Dictionary<string, Color> LeagueColors = new()
    {
        ["tur.1"] = Color.FromRgb(0xE3, 0x0A, 0x17),
        ["esp.1"] = Color.FromRgb(0xFA, 0xCC, 0x15),
        ["ita.1"] = Color.FromRgb(0x0E, 0xA5, 0xE9),
        ["ger.1"] = Color.FromRgb(0xD1, 0xD5, 0xDB),
        ["uefa.champions"] = Color.FromRgb(0x3B, 0x82, 0xF6),
        ["uefa.europa"] = Color.FromRgb(0xF9, 0x73, 0x16),
        ["uefa.europa.conf"] = Color.FromRgb(0x10, 0xB9, 0x81),
        ["uefa.nations"] = Color.FromRgb(0xA8, 0x55, 0xF7),
    };

    readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    readonly ScoreFeed _feed = new();
    Dictionary<string, List<Match>> _data = new();
    string _filter = "all";
    bool _loading, _scrollToToday = true;
    DateTime _renderedDay;
    FrameworkElement? _todayAnchor;

    public ScoresWidget()
    {
        InitializeComponent();
        BuildFilterBar();
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => await RefreshAsync();
        AppSettings.Changed += async () => { BuildFilterBar(); await RefreshAsync(); };
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(async () => await RefreshAsync());
        };
    }

    /// <summary>"Tümü" + one tab per enabled league, in the order of the settings.</summary>
    void BuildFilterBar()
    {
        var leagues = AppSettings.Current.Leagues;
        if (_filter != "all" && !leagues.Contains(_filter)) _filter = "all";
        FilterBar.Children.Clear();
        foreach (var (tag, label) in new[] { ("all", L.All) }.Concat(leagues.Select(l => (l, ScoreService.NameOf(l)))))
        {
            var rb = new RadioButton
            {
                Content = label, Tag = tag, GroupName = "scoreFilter",
                Style = (Style)FindResource("SegmentButton"), IsChecked = tag == _filter,
                Padding = new Thickness(18, 7, 18, 7),
            };
            rb.Checked += Filter_Checked;
            FilterBar.Children.Add(rb);
        }
    }

    /// <summary>Mouse wheel scrolls the tab strip sideways.</summary>
    void FilterScroll_Wheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        FilterScroll.ScrollToHorizontalOffset(FilterScroll.HorizontalOffset - e.Delta * 0.5);
        e.Handled = true;
    }

    void Filter_Checked(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)sender).BringIntoView();   // a half-hidden tab slides fully into view
        _filter = (string)((FrameworkElement)sender).Tag;
        _scrollToToday = true;
        if (IsLoaded) Render();
    }

    async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        _timer.Stop();
        bool anyOk = false;
        try
        {
            var s = AppSettings.Current;
            var r = await _feed.RefreshAsync(s.Leagues, Math.Clamp(s.ScoreDaysBack, 0, 7), Math.Clamp(s.ScoreDaysAhead, 0, 14));
            anyOk = r.AnyOk;
            if (anyOk) _data = r.ByLeague;

            SourceText.Text = anyOk ? $"ESPN · {DateTime.Now:HH:mm}" : L.Offline;
            SourceDot.Fill = (Brush)FindResource(anyOk ? "Accent" : "Danger");
            Render();
        }
        catch (Exception ex) { App.Log(ex); }
        finally
        {
            _loading = false;
            _timer.Interval = NextInterval(anyOk);
            _timer.Start();
        }
    }

    /// <summary>30 s while something is live, 1 min around kick-off, otherwise 10 min.</summary>
    TimeSpan NextInterval(bool ok)
    {
        if (!ok) return TimeSpan.FromMinutes(2);
        var now = DateTime.Now;
        var all = _data.Values.SelectMany(x => x).ToList();
        if (all.Any(m => m.State == "in")) return TimeSpan.FromSeconds(30);
        if (all.Any(m => m.State == "pre" && !m.Postponed && m.Kickoff - now < TimeSpan.FromMinutes(15) && now - m.Kickoff < TimeSpan.FromHours(3)))
            return TimeSpan.FromMinutes(1);
        var next = all.Where(m => m.State == "pre" && m.Kickoff > now).Select(m => m.Kickoff - now).DefaultIfEmpty(TimeSpan.MaxValue).Min();
        return next < TimeSpan.FromMinutes(25) ? next - TimeSpan.FromMinutes(14) : TimeSpan.FromMinutes(10);
    }

    /// <summary>Day-by-day feed: each day header, then its matches grouped by league.</summary>
    void Render()
    {
        double keepOffset = Scroller.VerticalOffset;
        var today = DateTime.Today;
        if (today != _renderedDay) { _scrollToToday = true; _renderedDay = today; }   // new day → jump to "Bugün"

        List.Children.Clear();
        _todayAnchor = null;
        int live = _data.Values.Sum(l => l.Count(m => m.State == "in"));
        LiveBadge.Visibility = live > 0 ? Visibility.Visible : Visibility.Collapsed;
        LiveCount.Text = L.LiveCount(live);

        var order = AppSettings.Current.Leagues;
        var matches = _data.Where(kv => _filter == "all" || _filter == kv.Key).SelectMany(kv => kv.Value);
        foreach (var day in matches.GroupBy(m => m.Kickoff.Date).OrderBy(g => g.Key))
        {
            var header = DayHeader(day.Key, today);
            if (_todayAnchor == null && day.Key >= today) _todayAnchor = header;
            List.Children.Add(header);
            foreach (var lg in day.GroupBy(m => m.League).OrderBy(g => order.IndexOf(g.Key)))
            {
                if (_filter == "all") List.Children.Add(LeagueHeader(lg.Key));
                foreach (var m in lg.OrderBy(m => m.Kickoff).ThenBy(m => m.Home)) List.Children.Add(Row(m));
            }
        }

        StatusMsg.Visibility = List.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusMsg.Text = _data.Count == 0 ? L.ScoresFailed : L.NoMatches;

        // after layout: first show / filter change / new day → scroll "Bugün" to the top; otherwise keep the user's position
        Dispatcher.BeginInvoke(() =>
        {
            if (_scrollToToday && List.Children.Count > 0)
            {
                double y = 0;
                if (_todayAnchor != null && _todayAnchor.IsDescendantOf(List))
                    y = _todayAnchor.TransformToAncestor(List).Transform(new Point()).Y;
                Scroller.ScrollToVerticalOffset(y);
                _scrollToToday = false;
            }
            else Scroller.ScrollToVerticalOffset(keepOffset);
        }, DispatcherPriority.Loaded);
    }

    FrameworkElement DayHeader(DateTime day, DateTime today)
    {
        var rel = day == today ? L.Today : day == today.AddDays(1) ? L.Tomorrow : day == today.AddDays(-1) ? L.Yesterday : null;
        var date = day.ToString(L.DayFormat, L.Culture);
        var tb = new TextBlock { FontSize = 13, Margin = new Thickness(2, List.Children.Count == 0 ? 0 : 16, 0, 6) };
        tb.Inlines.Add(new System.Windows.Documents.Run(rel ?? date)
        {
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource(day == today ? "Accent" : "Text1"),
        });
        if (rel != null)
            tb.Inlines.Add(new System.Windows.Documents.Run("  ·  " + date) { Foreground = (Brush)FindResource("Text3"), FontSize = 12 });
        return tb;
    }

    UIElement LeagueHeader(string league)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 0, 5) };
        var color = LeagueColors.TryGetValue(league, out var c) ? c : Colors.Gray;
        sp.Children.Add(new Rectangle { Width = 3, Height = 11, RadiusX = 1.5, RadiusY = 1.5, Fill = new SolidColorBrush(color), Margin = new Thickness(0, 0, 7, 0) });
        sp.Children.Add(new TextBlock
        {
            Text = L.Upper(ScoreService.NameOf(league)), FontSize = 10.5, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("Text2"), VerticalAlignment = VerticalAlignment.Center,
        });
        return sp;
    }

    UIElement Row(Match m)
    {
        bool live = m.State == "in", done = m.State == "post";
        var text1 = (Brush)FindResource("Text1");
        var text2 = (Brush)FindResource("Text2");
        var text3 = (Brush)FindResource("Text3");
        var accent = (Brush)FindResource("Accent");

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // status
        var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (live) status.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = accent, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        status.Children.Add(new TextBlock
        {
            Text = m.Status, FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = live ? accent : m.Postponed ? (Brush)FindResource("Danger") : text2,
        });
        g.Children.Add(status);

        bool homeDim = done && m.AwayWin, awayDim = done && m.HomeWin;
        g.Children.Add(Cell(Team(m.Home, homeDim ? text2 : text1, HorizontalAlignment.Right), 1));
        g.Children.Add(Cell(Logo(m.HomeLogo), 2));

        var score = new TextBlock
        {
            Text = m.State == "pre" ? "–" : $"{m.HomeScore}  -  {m.AwayScore}",
            FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Foreground = live ? accent : m.State == "pre" ? text3 : text1,
        };
        g.Children.Add(Cell(score, 3));

        g.Children.Add(Cell(Logo(m.AwayLogo), 4));
        g.Children.Add(Cell(Team(m.Away, awayDim ? text2 : text1, HorizontalAlignment.Left), 5));

        var card = new Border
        {
            Background = (Brush)FindResource("CardBg"),
            BorderBrush = live ? (Brush)FindResource("AccentDim") : (Brush)FindResource("CardBorder"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 6), Child = g,
        };
        var btn = new Button { Style = (Style)FindResource("RowButton"), Content = card, ToolTip = L.OpenMatch };
        btn.Click += (_, _) => Browser.Open(m.Url);
        return btn;
    }

    static UIElement Cell(UIElement e, int col) { Grid.SetColumn(e, col); return e; }

    static TextBlock Team(string name, Brush fg, HorizontalAlignment align) => new()
    {
        Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = fg,
        HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 6, 0),
    };

    static Image Logo(string url)
    {
        var img = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        _ = SetAsync(img, url);
        return img;
    }

    static async Task SetAsync(Image img, string url) => img.Source = await ImageCache.GetAsync(url, 48);
}
