using System.Globalization;
using System.Text.Json;

namespace MirageBox.Oasis.Plugins.Weather;

public record WeatherSnapshot(
    float Temperature,
    float FeelsLike,
    float Humidity,
    float WindSpeed,
    int WeatherCode,
    string Location,
    DateTimeOffset FetchedUtc);

/// <summary>
/// Minimal Open-Meteo client (free, no API key): geocoding + current conditions.
/// </summary>
public sealed class OpenMeteoClient : IDisposable
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "MirageBox.Oasis.Plugins.Weather" } },
    };

    public async Task<(float Lat, float Lon, string Name)?> GeocodeAsync(string place, CancellationToken ct)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(place)}&count=1";
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;
        var first = results[0];
        return (first.GetProperty("latitude").GetSingle(),
                first.GetProperty("longitude").GetSingle(),
                first.GetProperty("name").GetString() ?? place);
    }

    public async Task<WeatherSnapshot> GetCurrentAsync(float lat, float lon, bool imperial, string location, CancellationToken ct)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}") +
            "&current=temperature_2m,relative_humidity_2m,apparent_temperature,wind_speed_10m,weather_code";
        if (imperial)
            url += "&temperature_unit=fahrenheit&wind_speed_unit=mph";

        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
        var cur = doc.RootElement.GetProperty("current");
        return new WeatherSnapshot(
            cur.GetProperty("temperature_2m").GetSingle(),
            cur.GetProperty("apparent_temperature").GetSingle(),
            cur.GetProperty("relative_humidity_2m").GetSingle(),
            cur.GetProperty("wind_speed_10m").GetSingle(),
            cur.GetProperty("weather_code").GetInt32(),
            location,
            DateTimeOffset.UtcNow);
    }

    public void Dispose() => _http.Dispose();
}
