using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using OpsMonitor.Widget.Models;

namespace OpsMonitor.Widget.Services;

internal sealed class WeatherService : IDisposable
{
    private const string ArsoBase =
        "https://meteo.arso.gov.si/uploads/probase/www";
    private const string OpenMeteoBase = "https://api.open-meteo.com/v1";
    private const string OpenMeteoAirBase = "https://air-quality-api.open-meteo.com/v1";
    private const string OpenMeteoGeocodingBase = "https://geocoding-api.open-meteo.com/v1";
    private static readonly TimeSpan RadarCacheDuration = TimeSpan.FromMinutes(4);

    private readonly HttpClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly TimeSpan _refreshInterval;
    private WeatherLocation _location;
    private Task? _loop;
    private byte[]? _radarBytes;
    private DateTimeOffset _radarFetchedAt;
    private bool _disposed;

    public WeatherService(WeatherLocation location, TimeSpan refreshInterval)
    {
        _location = location;
        _refreshInterval = TimeSpan.FromMinutes(
            Math.Clamp(refreshInterval.TotalMinutes, 5, 60));
        _client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("OPS-Monitor", "1.0"));
    }

    public event EventHandler<WeatherSnapshot>? SnapshotAvailable;

    public WeatherSnapshot? Current { get; private set; }

    public WeatherLocation Location => _location;

    public void Start()
    {
        if (_loop is null)
        {
            _loop = RunAsync(_lifetime.Token);
        }
    }

    public async Task SetLocationAsync(WeatherLocation location, CancellationToken cancellationToken = default)
    {
        _location = location;
        Current = null;
        await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            var token = linked.Token;
            var location = _location;
            var forecastTask = FetchForecastAsync(location, token);
            var observationTask = FetchArsoObservationAsync(location, token);
            var airTask = FetchAirQualityAsync(location, token);
            var warningTask = FetchWarningAsync(location, token);

            var forecast = await forecastTask.ConfigureAwait(false);
            ArsoObservation? observation = await SafeAsync(observationTask).ConfigureAwait(false);
            if (observation?.ObservedAt is { } observedAt &&
                DateTimeOffset.Now - observedAt > TimeSpan.FromMinutes(45))
            {
                observation = null;
            }
            AirQualitySnapshot? air = await SafeAsync(airTask).ConfigureAwait(false);
            WeatherAlert? warning = await SafeAsync(warningTask).ConfigureAwait(false);

            var current = forecast.Current;
            var snapshot = new WeatherSnapshot(
                location,
                DateTimeOffset.Now,
                observation?.ObservedAt,
                observation is null ? "Open-Meteo model blend" : "ARSO live station",
                observation?.StationName ?? location.Name,
                observation?.TemperatureCelsius ?? current.TemperatureCelsius,
                current.FeelsLikeCelsius,
                observation?.RelativeHumidity ?? current.RelativeHumidity,
                observation?.WindKilometresPerHour ?? current.WindKilometresPerHour,
                observation?.WindDirection ?? current.WindDirection,
                observation?.PressureHectopascals,
                current.PrecipitationMillimetres,
                current.PrecipitationProbability,
                current.WeatherCode,
                air,
                warning,
                forecast.Hourly,
                forecast.Daily);
            Current = snapshot;
            SnapshotAvailable?.Invoke(this, snapshot);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or System.Xml.XmlException)
        {
            if (Current is { } lastGood)
            {
                Current = lastGood with { IsStale = true };
                SnapshotAvailable?.Invoke(this, Current);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<IReadOnlyList<WeatherLocation>> SearchLocationsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string url = $"{OpenMeteoGeocodingBase}/search?name={Uri.EscapeDataString(query.Trim())}" +
                     "&count=8&language=en&format=json";
        using JsonDocument document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("results", out JsonElement results))
        {
            return [];
        }

        var locations = new List<WeatherLocation>();
        foreach (JsonElement item in results.EnumerateArray())
        {
            string name = GetString(item, "name") ?? string.Empty;
            string country = GetString(item, "country") ?? GetString(item, "country_code") ?? string.Empty;
            double? latitude = GetDouble(item, "latitude");
            double? longitude = GetDouble(item, "longitude");
            if (string.IsNullOrWhiteSpace(name) || latitude is null || longitude is null)
            {
                continue;
            }

            locations.Add(new WeatherLocation(
                name,
                country,
                latitude.Value,
                longitude.Value,
                GetString(item, "timezone") ?? "auto",
                IsCelje(name, latitude.Value, longitude.Value) ? "CELJE_MEDLOG" : null));
        }

        return locations;
    }

    public async Task<byte[]> GetRadarAnimationAsync(CancellationToken cancellationToken = default)
    {
        if (_radarBytes is not null && DateTimeOffset.Now - _radarFetchedAt < RadarCacheDuration)
        {
            return _radarBytes;
        }

        const string url =
            "https://meteo.arso.gov.si/uploads/probase/www/observ/radar/si0-rm-anim.gif";
        _radarBytes = await _client.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        _radarFetchedAt = DateTimeOffset.Now;
        return _radarBytes;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(_refreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<ForecastResult> FetchForecastAsync(
        WeatherLocation location,
        CancellationToken cancellationToken)
    {
        string coordinates = Coordinates(location);
        string url = $"{OpenMeteoBase}/forecast?{coordinates}" +
                     "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m,wind_direction_10m" +
                     "&hourly=temperature_2m,apparent_temperature,precipitation_probability,precipitation,weather_code,wind_speed_10m" +
                     "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,wind_speed_10m_max,uv_index_max,sunrise,sunset" +
                     "&timezone=auto&forecast_days=8";
        using JsonDocument document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        JsonElement current = root.GetProperty("current");
        int weatherCode = GetInt(current, "weather_code") ?? 0;
        double windDirectionDegrees = GetDouble(current, "wind_direction_10m") ?? 0;
        var currentResult = new CurrentForecast(
            GetDouble(current, "temperature_2m") ?? 0,
            GetDouble(current, "apparent_temperature") ?? 0,
            GetInt(current, "relative_humidity_2m") ?? 0,
            GetDouble(current, "wind_speed_10m") ?? 0,
            CompassDirection(windDirectionDegrees),
            GetDouble(current, "precipitation") ?? 0,
            0,
            weatherCode);

        JsonElement hourly = root.GetProperty("hourly");
        string[] hourTimes = ReadStringArray(hourly, "time");
        double[] hourTemps = ReadDoubleArray(hourly, "temperature_2m");
        double[] hourFeels = ReadDoubleArray(hourly, "apparent_temperature");
        int[] hourRainChance = ReadIntArray(hourly, "precipitation_probability");
        double[] hourRain = ReadDoubleArray(hourly, "precipitation");
        int[] hourCodes = ReadIntArray(hourly, "weather_code");
        double[] hourWind = ReadDoubleArray(hourly, "wind_speed_10m");
        int count = MinLength(hourTimes.Length, hourTemps.Length, hourFeels.Length,
            hourRainChance.Length, hourRain.Length, hourCodes.Length, hourWind.Length);
        var hours = new List<WeatherHour>(Math.Min(count, 25));
        DateTime now = DateTime.Now.AddMinutes(-30);
        for (var index = 0; index < count && hours.Count < 25; index++)
        {
            if (!DateTime.TryParse(hourTimes[index], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out DateTime time) || time < now)
            {
                continue;
            }

            hours.Add(new WeatherHour(time, hourTemps[index], hourFeels[index],
                hourRainChance[index], hourRain[index], hourWind[index], hourCodes[index]));
        }

        if (hours.Count > 0)
        {
            currentResult = currentResult with
            {
                PrecipitationProbability = hours[0].PrecipitationProbability
            };
        }

        JsonElement daily = root.GetProperty("daily");
        string[] dayTimes = ReadStringArray(daily, "time");
        double[] dayMax = ReadDoubleArray(daily, "temperature_2m_max");
        double[] dayMin = ReadDoubleArray(daily, "temperature_2m_min");
        int[] dayRain = ReadIntArray(daily, "precipitation_probability_max");
        double[] dayWind = ReadDoubleArray(daily, "wind_speed_10m_max");
        double[] dayUv = ReadDoubleArray(daily, "uv_index_max");
        int[] dayCodes = ReadIntArray(daily, "weather_code");
        string[] sunrises = ReadStringArray(daily, "sunrise");
        string[] sunsets = ReadStringArray(daily, "sunset");
        count = MinLength(dayTimes.Length, dayMax.Length, dayMin.Length, dayRain.Length,
            dayWind.Length, dayUv.Length, dayCodes.Length, sunrises.Length, sunsets.Length);
        var days = new List<WeatherDay>(count);
        for (var index = 0; index < count; index++)
        {
            if (!DateTime.TryParse(dayTimes[index], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out DateTime date))
            {
                continue;
            }

            _ = DateTime.TryParse(sunrises[index], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime sunrise);
            _ = DateTime.TryParse(sunsets[index], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime sunset);
            days.Add(new WeatherDay(date, dayMin[index], dayMax[index], dayRain[index],
                dayWind[index], dayUv[index], dayCodes[index], sunrise, sunset));
        }

        return new ForecastResult(currentResult, hours, days);
    }

    private async Task<ArsoObservation?> FetchArsoObservationAsync(
        WeatherLocation location,
        CancellationToken cancellationToken)
    {
        if (!IsSlovenian(location))
        {
            return null;
        }

        string endpoint = location.ArsoStationCode is { Length: > 0 } stationCode
            ? $"{ArsoBase}/observ/surface/text/sl/observationAms_{stationCode}_latest.xml"
            : $"{ArsoBase}/observ/surface/text/sl/observationAms_si_latest.xml";
        string xml = await _client.GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return ParseArsoObservation(xml, location);
    }

    internal static ArsoObservation? ParseArsoObservation(string xml, WeatherLocation location)
    {
        XDocument document = XDocument.Parse(xml);
        XElement? best = document.Descendants("metData")
            .Select(element => new
            {
                Element = element,
                Latitude = ReadDouble(element, "domain_lat"),
                Longitude = ReadDouble(element, "domain_lon")
            })
            .Where(item => item.Latitude is not null && item.Longitude is not null)
            .OrderBy(item => HaversineKilometres(
                location.Latitude, location.Longitude,
                item.Latitude!.Value, item.Longitude!.Value))
            .Select(item => item.Element)
            .FirstOrDefault();
        if (best is null || ReadDouble(best, "t") is not { } temperature)
        {
            return null;
        }

        DateTimeOffset? observedAt = DateTimeOffset.TryParse(
            ReadText(best, "tsValid_issued_RFC822"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out DateTimeOffset parsed)
            ? parsed
            : null;
        return new ArsoObservation(
            ReadText(best, "domain_longTitle") ?? location.Name,
            temperature,
            (int)Math.Round(ReadDouble(best, "rh") ?? 0),
            ReadDouble(best, "ff_val_kmh") ?? 0,
            ReadText(best, "dd_shortText") ?? ReadText(best, "dd_icon") ?? string.Empty,
            ReadDouble(best, "msl") ?? ReadDouble(best, "p"),
            observedAt);
    }

    private async Task<AirQualitySnapshot?> FetchAirQualityAsync(
        WeatherLocation location,
        CancellationToken cancellationToken)
    {
        string url = $"{OpenMeteoAirBase}/air-quality?{Coordinates(location)}" +
                     "&current=european_aqi,pm2_5,pm10,uv_index,grass_pollen,birch_pollen&timezone=auto";
        using JsonDocument document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("current", out JsonElement current))
        {
            return null;
        }

        return new AirQualitySnapshot(
            GetInt(current, "european_aqi"),
            GetDouble(current, "pm2_5"),
            GetDouble(current, "pm10"),
            GetDouble(current, "uv_index"),
            GetDouble(current, "grass_pollen"),
            GetDouble(current, "birch_pollen"));
    }

    private async Task<WeatherAlert?> FetchWarningAsync(
        WeatherLocation location,
        CancellationToken cancellationToken)
    {
        if (!IsSlovenian(location))
        {
            return null;
        }

        string region = WarningRegion(location);
        string url = $"{ArsoBase}/warning/text/sl/warning_{region}_latest_CAP.xml";
        string xml = await _client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseWarning(xml);
    }

    internal static WeatherAlert? ParseWarning(string xml)
    {
        XDocument document = XDocument.Parse(xml);
        XElement? info = document.Descendants()
            .Where(element => element.Name.LocalName == "info")
            .FirstOrDefault(element => string.Equals(
                element.Elements().FirstOrDefault(child => child.Name.LocalName == "language")?.Value,
                "en-GB",
                StringComparison.OrdinalIgnoreCase))
            ?? document.Descendants().FirstOrDefault(element => element.Name.LocalName == "info");
        if (info is null)
        {
            return null;
        }

        string? awareness = info.Elements()
            .Where(element => element.Name.LocalName == "parameter")
            .FirstOrDefault(element => element.Elements().Any(child =>
                child.Name.LocalName == "valueName" && child.Value == "awareness_level"))?
            .Elements().FirstOrDefault(child => child.Name.LocalName == "value")?.Value;
        int level = int.TryParse(awareness?.Split(';')[0], out int parsedLevel) ? parsedLevel : 1;
        return new WeatherAlert(
            LocalText(info, "headline") ?? LocalText(info, "event") ?? "ARSO weather notice",
            LocalText(info, "description")?.Trim() ?? string.Empty,
            Math.Clamp(level, 1, 4),
            ParseOffset(LocalText(info, "onset")),
            ParseOffset(LocalText(info, "expires")));
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using Stream stream = await _client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<T?> SafeAsync<T>(Task<T?> task) where T : class
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string Coordinates(WeatherLocation location) =>
        FormattableString.Invariant($"latitude={location.Latitude:0.####}&longitude={location.Longitude:0.####}");

    private static bool IsSlovenian(WeatherLocation location) =>
        location.Country.Contains("Sloven", StringComparison.OrdinalIgnoreCase) ||
        (location.Latitude is >= 45.35 and <= 46.9 && location.Longitude is >= 13.35 and <= 16.65);

    private static bool IsCelje(string name, double latitude, double longitude) =>
        name.Contains("Celje", StringComparison.OrdinalIgnoreCase) ||
        HaversineKilometres(latitude, longitude, 46.2366, 15.2259) < 12;

    private static string WarningRegion(WeatherLocation location)
    {
        if (location.Longitude < 14.3)
        {
            return location.Latitude > 46.15 ? "SLOVENIA_NORTH-WEST" : "SLOVENIA_SOUTH-WEST";
        }

        if (location.Latitude < 45.9 && location.Longitude > 14.55)
        {
            return "SLOVENIA_SOUTH-EAST";
        }

        if (location.Longitude > 15.05 || location.Latitude > 46.42)
        {
            return "SLOVENIA_NORTH-EAST";
        }

        return "SLOVENIA_MIDDLE";
    }

    private static string CompassDirection(double degrees)
    {
        string[] directions = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        int index = (int)Math.Round((degrees % 360) / 45, MidpointRounding.AwayFromZero) % 8;
        return directions[index];
    }

    private static double HaversineKilometres(double lat1, double lon1, double lat2, double lon2)
    {
        const double radius = 6371;
        double dLat = DegreesToRadians(lat2 - lat1);
        double dLon = DegreesToRadians(lon2 - lon1);
        double a = Math.Pow(Math.Sin(dLat / 2), 2) +
                   Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                   Math.Pow(Math.Sin(dLon / 2), 2);
        return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private static string? ReadText(XElement element, string name) =>
        element.Element(name)?.Value is { Length: > 0 } value ? value : null;

    private static string? LocalText(XElement element, string localName) =>
        element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value;

    private static double? ReadDouble(XElement element, string name) =>
        double.TryParse(ReadText(element, name), NumberStyles.Float, CultureInfo.InvariantCulture,
            out double value) && double.IsFinite(value) ? value : null;

    private static DateTimeOffset? ParseOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces,
            out DateTimeOffset parsed) ? parsed : null;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double result) &&
        double.IsFinite(result) ? result : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : GetDouble(element, name) is { } number ? (int)Math.Round(number) : null;

    private static string[] ReadStringArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement values)
            ? values.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray()
            : [];

    private static double[] ReadDoubleArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement values)
            ? values.EnumerateArray().Select(value => value.TryGetDouble(out double number) ? number : 0).ToArray()
            : [];

    private static int[] ReadIntArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement values)
            ? values.EnumerateArray().Select(value => value.TryGetInt32(out int number) ? number : 0).ToArray()
            : [];

    private static int MinLength(params int[] lengths) => lengths.Length == 0 ? 0 : lengths.Min();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _client.Dispose();
        _lifetime.Dispose();
        _refreshGate.Dispose();
    }

    internal sealed record ArsoObservation(
        string StationName,
        double TemperatureCelsius,
        int RelativeHumidity,
        double WindKilometresPerHour,
        string WindDirection,
        double? PressureHectopascals,
        DateTimeOffset? ObservedAt);

    private sealed record CurrentForecast(
        double TemperatureCelsius,
        double FeelsLikeCelsius,
        int RelativeHumidity,
        double WindKilometresPerHour,
        string WindDirection,
        double PrecipitationMillimetres,
        int PrecipitationProbability,
        int WeatherCode);

    private sealed record ForecastResult(
        CurrentForecast Current,
        IReadOnlyList<WeatherHour> Hourly,
        IReadOnlyList<WeatherDay> Daily);
}
