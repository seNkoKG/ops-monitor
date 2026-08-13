namespace OpsMonitor.Widget.Models;

public sealed record WeatherLocation(
    string Name,
    string Country,
    double Latitude,
    double Longitude,
    string TimeZone,
    string? ArsoStationCode = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Country)
        ? Name
        : $"{Name}, {Country}";
}

public sealed record WeatherHour(
    DateTime Time,
    double TemperatureCelsius,
    double FeelsLikeCelsius,
    int PrecipitationProbability,
    double PrecipitationMillimetres,
    double WindKilometresPerHour,
    double WindGustKilometresPerHour,
    int RelativeHumidity,
    double DewPointCelsius,
    double VisibilityKilometres,
    int CloudCover,
    int ConfidenceScore,
    int WeatherCode)
{
    public string TimeLabel => Time.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    public string TemperatureLabel => $"{Math.Round(TemperatureCelsius):0}°";

    public string RainLabel => $"{PrecipitationProbability}%";

    public string DetailLabel => $"RH {RelativeHumidity}% · G {WindGustKilometresPerHour:0}";

    public string ConfidenceLabel => $"{ConfidenceScore}%";

    public string Icon => WeatherPresentation.Icon(WeatherCode, Time.Hour is >= 6 and < 21);
}

public sealed record WeatherMinute(
    DateTime Time,
    double PrecipitationMillimetres,
    int PrecipitationProbability,
    int ConfidenceScore,
    int WeatherCode)
{
    public string TimeLabel => Time.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    public string RainLabel => PrecipitationMillimetres < 0.05
        ? $"{PrecipitationProbability}%"
        : $"{PrecipitationMillimetres:0.0} mm";

    public string Icon => WeatherPresentation.Icon(WeatherCode, Time.Hour is >= 6 and < 21);
}

public sealed record WeatherDay(
    DateTime Date,
    double MinimumCelsius,
    double MaximumCelsius,
    int PrecipitationProbability,
    double PrecipitationMillimetres,
    double WindKilometresPerHour,
    double WindGustKilometresPerHour,
    double UvIndex,
    int ConfidenceScore,
    int WeatherCode,
    DateTime? Sunrise,
    DateTime? Sunset)
{
    public string DayLabel => Date.Date == DateTime.Today
        ? "TODAY"
        : Date.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture).ToUpperInvariant();

    public string RangeLabel => $"{Math.Round(MinimumCelsius):0}°  /  {Math.Round(MaximumCelsius):0}°";

    public string RainLabel => PrecipitationMillimetres < 0.05
        ? $"{PrecipitationProbability}% rain"
        : $"{PrecipitationProbability}% · {PrecipitationMillimetres:0.#} mm";

    public string WindLabel => $"{WindKilometresPerHour:0} · gust {WindGustKilometresPerHour:0} km/h";

    public string ConfidenceLabel => $"{ConfidenceScore}% agreement";

    public string Icon => WeatherPresentation.Icon(WeatherCode, true);
}

public sealed record ForecastConfidence(
    int Score,
    double TemperatureSpreadCelsius,
    int PrecipitationSpreadPercent,
    int ModelCount)
{
    public string Label => Score switch
    {
        >= 85 => "HIGH CONFIDENCE",
        >= 70 => "GOOD CONFIDENCE",
        >= 50 => "MIXED SIGNAL",
        _ => "LOW CONFIDENCE"
    };

    public string Detail =>
        $"{ModelCount} models · ±{TemperatureSpreadCelsius / 2:0.0}° · rain spread {PrecipitationSpreadPercent}%";
}

public sealed record OfficialWeatherOutlook(
    string Region,
    string Summary,
    DateTimeOffset? IssuedAt)
{
    public string SourceLabel => IssuedAt is { } issued
        ? $"ARSO {Region} · issued {issued.LocalDateTime:t}"
        : $"ARSO {Region}";
}

public sealed record WeatherAlert(
    string Headline,
    string Description,
    int Level,
    DateTimeOffset? Starts,
    DateTimeOffset? Expires)
{
    public bool IsActive => Expires is null || Expires > DateTimeOffset.Now;

    public string LevelLabel => Level switch
    {
        >= 4 => "EXTREME",
        3 => "SEVERE",
        2 => "ELEVATED",
        _ => "MINOR"
    };
}

public sealed record AirQualitySnapshot(
    int? EuropeanAqi,
    double? Pm25,
    double? Pm10,
    double? UvIndex,
    double? GrassPollen,
    double? BirchPollen)
{
    public string AqiLabel => EuropeanAqi switch
    {
        null => "N/A",
        <= 20 => "Good",
        <= 40 => "Fair",
        <= 60 => "Moderate",
        <= 80 => "Poor",
        <= 100 => "Very poor",
        _ => "Extremely poor"
    };
}

public sealed record WeatherSnapshot(
    WeatherLocation Location,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ObservationTime,
    string ObservationSource,
    string StationName,
    double TemperatureCelsius,
    double FeelsLikeCelsius,
    int RelativeHumidity,
    double WindKilometresPerHour,
    double WindGustKilometresPerHour,
    string WindDirection,
    double? PressureHectopascals,
    double? DewPointCelsius,
    double? VisibilityKilometres,
    int CloudCover,
    double PrecipitationMillimetres,
    int PrecipitationProbability,
    int WeatherCode,
    AirQualitySnapshot? AirQuality,
    WeatherAlert? Alert,
    ForecastConfidence Confidence,
    OfficialWeatherOutlook? OfficialOutlook,
    IReadOnlyList<WeatherMinute> Nowcast,
    IReadOnlyList<WeatherHour> Hourly,
    IReadOnlyList<WeatherDay> Daily,
    bool IsStale = false)
{
    public string TemperatureLabel => $"{Math.Round(TemperatureCelsius):0}°";

    public string Condition => WeatherPresentation.Condition(WeatherCode);

    public string Icon => WeatherPresentation.Icon(WeatherCode, DateTime.Now.Hour is >= 6 and < 21);

    public string FeelsLikeLabel => $"Feels {Math.Round(FeelsLikeCelsius):0}°";

    public string HumidityLabel => $"{RelativeHumidity}%";

    public string WindLabel => string.IsNullOrWhiteSpace(WindDirection)
        ? $"{WindKilometresPerHour:0} km/h"
        : $"{WindDirection} {WindKilometresPerHour:0} km/h";

    public string GustLabel => $"{WindGustKilometresPerHour:0} km/h";

    public string DewPointLabel => DewPointCelsius is { } dewPoint
        ? $"{dewPoint:0}°"
        : "N/A";

    public string VisibilityLabel => VisibilityKilometres is { } visibility
        ? visibility >= 10 ? $"{visibility:0} km" : $"{visibility:0.0} km"
        : "N/A";

    public string CloudLabel => $"{CloudCover}%";

    public string PressureLabel => PressureHectopascals is { } pressure
        ? $"{pressure:0} hPa"
        : "N/A";

    public string RainLabel => $"{PrecipitationProbability}%";

    public string CoordinateLabel =>
        $"{Location.Latitude:0.0000}, {Location.Longitude:0.0000}";

    public string FreshnessLabel => IsStale
        ? $"Last good update {UpdatedAt.LocalDateTime:t}"
        : ObservationTime is { } observed
            ? $"Observed {observed.LocalDateTime:t} · refreshed {UpdatedAt.LocalDateTime:t}"
            : $"Forecast refreshed {UpdatedAt.LocalDateTime:t}";
}

public static class WeatherPresentation
{
    public static string Condition(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 or 56 or 57 => "Drizzle",
        61 or 63 or 65 or 66 or 67 => "Rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 or 96 or 99 => "Thunderstorms",
        _ => "Variable"
    };

    public static string Icon(int code, bool isDay) => code switch
    {
        0 => isDay ? "☀" : "☾",
        1 => isDay ? "🌤" : "☾",
        2 => "⛅",
        3 => "☁",
        45 or 48 => "≋",
        51 or 53 or 55 or 56 or 57 => "🌦",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "🌧",
        71 or 73 or 75 or 77 or 85 or 86 => "❄",
        95 or 96 or 99 => "⛈",
        _ => "◌"
    };
}
