namespace MirageBox.Oasis.Plugins.Weather;

public enum WeatherIcon { Sun, PartlyCloudy, Cloud, Fog, Rain, Snow, Thunder }

/// <summary>
/// WMO weather interpretation codes as used by Open-Meteo's `weather_code`.
/// https://open-meteo.com/en/docs — WMO Weather interpretation codes table.
/// </summary>
public static class WmoCodes
{
    public static (string Text, WeatherIcon Icon) Describe(int code) => code switch
    {
        0 => ("Clear", WeatherIcon.Sun),
        1 => ("Mostly clear", WeatherIcon.Sun),
        2 => ("Partly cloudy", WeatherIcon.PartlyCloudy),
        3 => ("Overcast", WeatherIcon.Cloud),
        45 or 48 => ("Fog", WeatherIcon.Fog),
        51 or 53 or 55 => ("Drizzle", WeatherIcon.Rain),
        56 or 57 => ("Freezing drizzle", WeatherIcon.Rain),
        61 or 63 or 65 => ("Rain", WeatherIcon.Rain),
        66 or 67 => ("Freezing rain", WeatherIcon.Rain),
        71 or 73 or 75 => ("Snow", WeatherIcon.Snow),
        77 => ("Snow grains", WeatherIcon.Snow),
        80 or 81 or 82 => ("Showers", WeatherIcon.Rain),
        85 or 86 => ("Snow showers", WeatherIcon.Snow),
        95 => ("Thunderstorm", WeatherIcon.Thunder),
        96 or 99 => ("Thunderstorm + hail", WeatherIcon.Thunder),
        _ => ("Unknown", WeatherIcon.Cloud),
    };
}
