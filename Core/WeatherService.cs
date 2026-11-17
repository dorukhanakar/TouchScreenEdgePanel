using System.Globalization;
using System.Text.Json;

namespace EdgePanel.Core;

public sealed record WeatherNow(double Temperature, int Code, bool IsDay);
public sealed record Place(string Name, string Detail, double Latitude, double Longitude);

/// <summary>Open-Meteo: free, no API key.</summary>
public static class WeatherService
{
    public static async Task<WeatherNow> GetAsync(double lat, double lon)
    {
        var url = string.Format(CultureInfo.InvariantCulture,
            "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code,is_day&timezone=auto",
            lat, lon);
        using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync(url));
        var c = doc.RootElement.GetProperty("current");
        return new WeatherNow(
            c.GetProperty("temperature_2m").GetDouble(),
            c.GetProperty("weather_code").GetInt32(),
            c.GetProperty("is_day").GetInt32() == 1);
    }

    public static async Task<List<Place>> SearchAsync(string query)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=8&language=" + L.Lang + "&name=" + Uri.EscapeDataString(query.Trim());
        using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync(url));
        var list = new List<Place>();
        if (!doc.RootElement.TryGetProperty("results", out var res)) return list;
        foreach (var r in res.EnumerateArray())
        {
            string Str(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            var detail = string.Join(", ", new[] { Str("admin1"), Str("country") }.Where(s => s.Length > 0));
            list.Add(new Place(Str("name"), detail, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble()));
        }
        return list;
    }

    public static string Describe(int code) => L.Weather(code);
}
