using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EdgePanel.Core;

namespace EdgePanel.Widgets;

public partial class HeaderClock : UserControl
{
    readonly DispatcherTimer _clock = new(DispatcherPriority.Background);
    readonly DispatcherTimer _weather = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(15) };

    public HeaderClock()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => Tick();
        _weather.Tick += async (_, _) => await LoadWeatherAsync();
        Loaded += async (_, _) =>
        {
            Tick();
            _weather.Start();
            await LoadWeatherAsync();
        };
        AppSettings.Changed += async () => await LoadWeatherAsync();
        // wake from sleep → refresh immediately
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                Dispatcher.BeginInvoke(async () => { Tick(); await LoadWeatherAsync(); });
        };
    }

    /// <summary>Redraws once per minute, aligned to the minute boundary (no per-second work).</summary>
    void Tick()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString(L.HeaderDateFormat, L.Culture);
        _clock.Interval = TimeSpan.FromMilliseconds(60_000 - now.Second * 1000 - now.Millisecond + 30);
        _clock.Start();
    }

    async Task LoadWeatherAsync()
    {
        var s = AppSettings.Current;
        CityText.Text = s.City;
        try
        {
            var w = await WeatherService.GetAsync(s.Latitude, s.Longitude);
            TempText.Text = $"{Math.Round(w.Temperature):0}°";
            WeatherIconHost.Content = WeatherIcon.Create(w.Code, w.IsDay);
            WeatherPanel.ToolTip = $"{s.City}: {L.Weather(w.Code)}, {w.Temperature:0.#}°C";
        }
        catch
        {
            TempText.Text = "--°";
            WeatherPanel.ToolTip = L.WeatherFailed;
        }
    }

    void Settings_Click(object sender, RoutedEventArgs e) => SettingsWindow.ShowSingle();
}
