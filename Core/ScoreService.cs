using System.Globalization;
using System.Text.Json;

namespace EdgePanel.Core;

public sealed class Match
{
    public string Id = "";
    public string League = "";
    public DateTime Kickoff;          // local time
    public string State = "pre";      // pre | in | post
    public string Status = "";        // what to show in the status column
    public bool Postponed;
    public string Home = "", Away = "";
    public string HomeLogo = "", AwayLogo = "";
    public string HomeScore = "", AwayScore = "";
    public bool HomeWin, AwayWin;
    public string Url = "";           // ESPN match summary page
}

/// <summary>
/// Live scores from ESPN's public scoreboard JSON (free, no key). The default endpoint returns the
/// league's current matchday: today's matches if there are any, otherwise the nearest round.
/// Swapping the data source only means changing this file.
/// </summary>
public static class ScoreService
{
    public static readonly Dictionary<string, string> LeagueNames = new()
    {
        ["tur.1"] = "Süper Lig",
        ["esp.1"] = "La Liga",
        ["ita.1"] = "Serie A",
        ["ger.1"] = "Bundesliga",
        ["uefa.champions"] = "Şampiyonlar Ligi",
        ["uefa.europa"] = "Avrupa Ligi",
        ["uefa.europa.conf"] = "Konferans Ligi",
        ["uefa.nations"] = "Uluslar Ligi",
    };


    public static string NameOf(string code) => L.League(code);

    /// <param name="day">A single day (ESPN rejects date ranges). null = the league's current/next match day.</param>
    public static async Task<List<Match>> GetAsync(string league, DateTime? day = null)
    {
        var url = $"https://site.api.espn.com/apis/site/v2/sports/soccer/{league}/scoreboard";
        if (day != null) url += "?dates=" + day.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync(url));
        var list = new List<Match>();
        if (!doc.RootElement.TryGetProperty("events", out var events)) return list;

        foreach (var e in events.EnumerateArray())
        {
            try { list.Add(Parse(e, league)); } catch { /* skip malformed event */ }
        }
        return list;
    }

    static string Str(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    static Match Parse(JsonElement e, string league)
    {
        var m = new Match { Id = Str(e, "id"), League = league };
        if (DateTimeOffset.TryParse(Str(e, "date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            m.Kickoff = dto.LocalDateTime;

        var status = e.GetProperty("status");
        var type = status.GetProperty("type");
        m.State = Str(type, "state");
        var name = Str(type, "name");
        var shortDetail = Str(type, "shortDetail");
        var clock = Str(status, "displayClock");

        m.Postponed = name is "STATUS_POSTPONED" or "STATUS_CANCELED" or "STATUS_ABANDONED" or "STATUS_SUSPENDED";
        m.Status = m.State switch
        {
            "in" => name == "STATUS_HALFTIME" || shortDetail == "HT" ? L.HalfTime
                  : name.Contains("PENALT") ? L.Penalties
                  : string.IsNullOrEmpty(clock) ? shortDetail : clock,
            "post" => name switch
            {
                "STATUS_FINAL_AET" => L.AfterExtraTime,
                "STATUS_FINAL_PEN" => L.Penalties,
                _ when m.Postponed => L.Postponed,
                _ => L.FullTime,
            },
            _ => m.Postponed ? L.Postponed : m.Kickoff.ToString("HH:mm"),
        };

        m.Url = "https://www.espn.com/soccer/match/_/gameId/" + m.Id;
        if (e.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
            foreach (var l in links.EnumerateArray())
                if (l.TryGetProperty("rel", out var rel) && rel.ValueKind == JsonValueKind.Array &&
                    rel.EnumerateArray().Any(r => r.GetString() == "summary") &&
                    Str(l, "href") is { Length: > 0 } href && href.StartsWith("https://www.espn.com/", StringComparison.OrdinalIgnoreCase))
                { m.Url = href; break; }

        var comp = e.GetProperty("competitions")[0];
        foreach (var c in comp.GetProperty("competitors").EnumerateArray())
        {
            var team = c.GetProperty("team");
            var teamName = Str(team, "shortDisplayName");
            if (teamName.Length == 0) teamName = Str(team, "displayName");
            var logo = SmallLogo(Str(team, "logo"));
            var score = Str(c, "score");
            bool win = c.TryGetProperty("winner", out var w) && w.ValueKind == JsonValueKind.True;
            if (Str(c, "homeAway") == "home") { m.Home = teamName; m.HomeLogo = logo; m.HomeScore = score; m.HomeWin = win; }
            else { m.Away = teamName; m.AwayLogo = logo; m.AwayScore = score; m.AwayWin = win; }
        }
        return m;
    }

    /// <summary>ESPN logos are 500px; its resizer serves a ~3 KB 64px version instead.</summary>
    static string SmallLogo(string url)
    {
        const string prefix = "https://a.espncdn.com/";
        if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return url;
        return "https://a.espncdn.com/combiner/i?img=/" + url[prefix.Length..] + "&w=64&h=64";
    }
}

public static class Browser
{
    /// <summary>Opens an https link in the Windows default browser.</summary>
    public static void Open(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            Native.AllowSetForegroundWindow(-1);   // let the browser come to the front
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
