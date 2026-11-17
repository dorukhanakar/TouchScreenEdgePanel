namespace EdgePanel.Core;

/// <summary>
/// Builds the multi-day score feed, one ESPN request per league per day (the API has no range query):
///   • a fixed window: yesterday … +3 days, and
///   • each league's whole current/next round (ESPN's next match day + the 3 days after it),
/// so a weekend round that starts on Friday shows Friday–Monday, not just the part inside the window.
/// Each day is cached and only re-fetched when it can have changed, so a refresh while something is
/// live costs a couple of requests, not dozens.
/// </summary>
public sealed class ScoreFeed
{
    const int RoundDays = 4;   // Fri–Mon league rounds, Tue–Wed / Thu European nights

    sealed class Entry
    {
        public List<Match> Matches = new();
        public DateTime Fetched;
    }

    readonly Dictionary<(string League, DateTime Day), Entry> _days = new();
    readonly Dictionary<string, (DateTime? Day, DateTime Fetched)> _nextDay = new();

    public sealed record Result(Dictionary<string, List<Match>> ByLeague, bool AnyOk);

    public async Task<Result> RefreshAsync(IReadOnlyList<string> leagues, int daysBack, int daysAhead)
    {
        var today = DateTime.Today;
        var window = Enumerable.Range(-daysBack, daysBack + daysAhead + 1).Select(i => today.AddDays(i)).ToList();

        // forget days that slid out of the feed and leagues that were switched off
        foreach (var key in _days.Keys.Where(k => k.Day < today.AddDays(-daysBack) || !leagues.Contains(k.League)).ToList())
            _days.Remove(key);

        // which days to show per league = window ∪ that league's round
        var next = await Task.WhenAll(leagues.Select(NextMatchDayAsync));
        var daysPerLeague = leagues.Select((l, i) =>
        {
            var days = new SortedSet<DateTime>(window);
            if (next[i] is { } d0)
                for (int k = 0; k < RoundDays; k++)
                    if (d0.AddDays(k) >= today.AddDays(-daysBack)) days.Add(d0.AddDays(k));
            return (League: l, Days: days);
        }).ToList();

        int ok = 0;
        var jobs = daysPerLeague.SelectMany(x => x.Days.Select(d => EnsureDayAsync(x.League, d))).ToList();
        foreach (var r in await Task.WhenAll(jobs)) if (r) ok++;

        var byLeague = new Dictionary<string, List<Match>>();
        foreach (var (league, days) in daysPerLeague)
        {
            byLeague[league] = days
                .SelectMany(d => _days.TryGetValue((league, d), out var e) ? e.Matches : new List<Match>())
                .GroupBy(m => m.Id).Select(g => g.First())
                .OrderBy(m => m.Kickoff).ToList();
        }
        return new Result(byLeague, ok > 0);
    }

    static bool IsStale(Entry e, DateTime day)
    {
        var now = DateTime.Now;
        if (e.Matches.Any(m => m.State == "in")) return true;
        // about to kick off, or should have started but ESPN still says "pre"
        if (e.Matches.Any(m => m.State == "pre" && !m.Postponed && m.Kickoff - now < TimeSpan.FromMinutes(15) && now - m.Kickoff < TimeSpan.FromHours(3)))
            return true;
        var age = now - e.Fetched;
        return day == DateTime.Today ? age > TimeSpan.FromMinutes(10) : age > TimeSpan.FromMinutes(30);
    }

    async Task<bool> EnsureDayAsync(string league, DateTime day)
    {
        if (_days.TryGetValue((league, day), out var e) && !IsStale(e, day)) return true;
        try
        {
            var matches = await ScoreService.GetAsync(league, day);
            _days[(league, day)] = new Entry { Matches = matches, Fetched = DateTime.Now };
            return true;
        }
        catch { return e != null; }   // keep showing the old copy on a network hiccup
    }

    /// <summary>ESPN's default scoreboard = the league's current (today) or next match day. Cached 30 min.</summary>
    async Task<DateTime?> NextMatchDayAsync(string league)
    {
        if (_nextDay.TryGetValue(league, out var c) && DateTime.Now - c.Fetched < TimeSpan.FromMinutes(30))
            return c.Day;
        try
        {
            var matches = await ScoreService.GetAsync(league);
            DateTime? d0 = matches.Count > 0 ? matches.Min(m => m.Kickoff).Date : null;
            _nextDay[league] = (d0, DateTime.Now);
            return d0;
        }
        catch { return c.Day; }
    }
}
