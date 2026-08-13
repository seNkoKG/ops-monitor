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
    private const string BestMatch = "best_match";
    private const string Ecmwf = "ecmwf_ifs025";
    private const string IconEurope = "dwd_icon_eu";
    private static readonly TimeSpan RadarCacheDuration = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan WeatherCacheMaximumAge = TimeSpan.FromHours(6);

    private readonly HttpClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly TimeSpan _refreshInterval;
    private readonly string _cachePath;
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
        _cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OPS Monitor",
            "Cache",
            "weather.json");
        _client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("OPS-Monitor", "1.0"));
        Current = LoadCached(location);
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
        Current = LoadCached(location);
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
            var outlookTask = FetchOfficialOutlookAsync(location, token);

            ForecastResult? forecast = await SafeAsync(forecastTask).ConfigureAwait(false);
            ArsoObservation? observation = await SafeAsync(observationTask).ConfigureAwait(false);
            if (observation?.ObservedAt is { } observedAt &&
                DateTimeOffset.Now - observedAt > TimeSpan.FromMinutes(45))
            {
                observation = null;
            }
            AirQualitySnapshot? air = await SafeAsync(airTask).ConfigureAwait(false);
            WeatherAlert? warning = await SafeAsync(warningTask).ConfigureAwait(false);
            OfficialWeatherOutlook? outlook = await SafeAsync(outlookTask).ConfigureAwait(false);

            if (forecast is null)
            {
                PublishStaleFallback(location, observation, air, warning, outlook);
                return;
            }

            var current = forecast.Current;
            bool usesStation = observation is not null;
            double temperature = observation?.TemperatureCelsius ?? current.TemperatureCelsius;
            int humidity = observation?.RelativeHumidity ?? current.RelativeHumidity;
            double wind = observation?.WindKilometresPerHour ?? current.WindKilometresPerHour;
            double feelsLike = usesStation
                ? CalculateApparentTemperature(temperature, humidity, wind)
                : current.FeelsLikeCelsius;
            var snapshot = new WeatherSnapshot(
                location,
                DateTimeOffset.Now,
                observation?.ObservedAt,
                observation is null
                    ? "Open-Meteo high-resolution blend"
                    : "ARSO live station + 3-model forecast",
                observation is { } observed
                    ? $"{observed.StationName} · {observed.DistanceKilometres:0.0} km"
                    : location.Name,
                temperature,
                feelsLike,
                humidity,
                wind,
                observation?.WindGustKilometresPerHour ?? current.WindGustKilometresPerHour,
                observation?.WindDirection ?? current.WindDirection,
                observation?.PressureHectopascals ?? current.PressureHectopascals,
                observation?.DewPointCelsius ?? current.DewPointCelsius,
                current.VisibilityKilometres,
                current.CloudCover,
                observation?.PrecipitationMillimetres ?? current.PrecipitationMillimetres,
                current.PrecipitationProbability,
                current.WeatherCode,
                air,
                warning,
                forecast.Confidence,
                outlook,
                forecast.Nowcast,
                forecast.Hourly,
                forecast.Daily);
            Current = snapshot;
            SaveCached(snapshot);
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

        if (TryParseCoordinates(query, out double exactLatitude, out double exactLongitude))
        {
            return
            [
                new WeatherLocation(
                    "Exact coordinates",
                    string.Empty,
                    exactLatitude,
                    exactLongitude,
                    "auto",
                    IsCelje("", exactLatitude, exactLongitude) ? "CELJE_MEDLOG" : null)
            ];
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

    private async Task<ForecastResult?> FetchForecastAsync(
        WeatherLocation location,
        CancellationToken cancellationToken)
    {
        string coordinates = Coordinates(location);
        string timeZone = Uri.EscapeDataString(
            string.IsNullOrWhiteSpace(location.TimeZone) ? "auto" : location.TimeZone);
        string url = $"{OpenMeteoBase}/forecast?{coordinates}" +
                     "&current=temperature_2m,relative_humidity_2m,apparent_temperature,dew_point_2m,precipitation,weather_code,cloud_cover,pressure_msl,visibility,wind_speed_10m,wind_direction_10m,wind_gusts_10m" +
                     "&minutely_15=precipitation,precipitation_probability,weather_code" +
                     "&hourly=temperature_2m,apparent_temperature,relative_humidity_2m,dew_point_2m,precipitation_probability,precipitation,weather_code,cloud_cover,visibility,wind_speed_10m,wind_gusts_10m" +
                     "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,wind_speed_10m_max,wind_gusts_10m_max,uv_index_max,sunrise,sunset" +
                     $"&models={BestMatch},{Ecmwf},{IconEurope}" +
                     $"&timezone={timeZone}&forecast_days=8&forecast_minutely_15=48";
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
            GetDouble(current, "wind_gusts_10m") ?? 0,
            CompassDirection(windDirectionDegrees),
            GetDouble(current, "pressure_msl"),
            GetDouble(current, "dew_point_2m"),
            GetDouble(current, "visibility") is { } visibilityMetres
                ? visibilityMetres / 1000
                : null,
            Math.Clamp(GetInt(current, "cloud_cover") ?? 0, 0, 100),
            GetDouble(current, "precipitation") ?? 0,
            0,
            weatherCode);

        JsonElement hourly = root.GetProperty("hourly");
        string[] hourTimes = ReadStringArray(hourly, "time");
        double[] hourTemps = ReadModelDoubleArray(hourly, "temperature_2m", BestMatch);
        double[] hourFeels = ReadModelDoubleArray(hourly, "apparent_temperature", BestMatch);
        int[] hourHumidity = ReadModelIntArray(hourly, "relative_humidity_2m", BestMatch);
        double[] hourDewPoint = ReadModelDoubleArray(hourly, "dew_point_2m", BestMatch);
        int[] hourRainChance = ReadModelIntArray(hourly, "precipitation_probability", BestMatch);
        double[] hourRain = ReadModelDoubleArray(hourly, "precipitation", BestMatch);
        int[] hourCodes = ReadModelIntArray(hourly, "weather_code", BestMatch);
        int[] hourCloud = ReadModelIntArray(hourly, "cloud_cover", BestMatch);
        double[] hourVisibility = ReadModelDoubleArray(hourly, "visibility", BestMatch);
        double[] hourWind = ReadModelDoubleArray(hourly, "wind_speed_10m", BestMatch);
        double[] hourGust = ReadModelDoubleArray(hourly, "wind_gusts_10m", BestMatch);
        double[][] modelTemps =
        [
            hourTemps,
            ReadModelDoubleArray(hourly, "temperature_2m", Ecmwf),
            ReadModelDoubleArray(hourly, "temperature_2m", IconEurope)
        ];
        int[][] modelRainChance =
        [
            hourRainChance,
            ReadModelIntArray(hourly, "precipitation_probability", Ecmwf),
            ReadModelIntArray(hourly, "precipitation_probability", IconEurope)
        ];
        int count = MinLength(hourTimes.Length, hourTemps.Length, hourFeels.Length,
            hourHumidity.Length, hourDewPoint.Length, hourRainChance.Length, hourRain.Length,
            hourCodes.Length, hourCloud.Length, hourVisibility.Length, hourWind.Length,
            hourGust.Length);
        var hours = new List<WeatherHour>(Math.Min(count, 25));
        DateTime now = DateTime.Now.AddMinutes(-30);
        var firstFutureHourIndex = 0;
        for (var index = 0; index < count && hours.Count < 25; index++)
        {
            if (!DateTime.TryParse(hourTimes[index], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out DateTime time) || time < now ||
                !HasHourlyValues(index, hourTemps, hourFeels, hourHumidity, hourDewPoint,
                    hourRainChance, hourRain, hourCodes, hourCloud, hourVisibility,
                    hourWind, hourGust))
            {
                continue;
            }

            if (hours.Count == 0)
            {
                firstFutureHourIndex = index;
            }

            hours.Add(new WeatherHour(time, hourTemps[index], hourFeels[index],
                hourRainChance[index], hourRain[index], hourWind[index], hourGust[index],
                hourHumidity[index], hourDewPoint[index], hourVisibility[index] / 1000,
                hourCloud[index], ConfidenceScore(index, modelTemps, modelRainChance),
                hourCodes[index]));
        }

        if (hours.Count > 0)
        {
            currentResult = currentResult with
            {
                PrecipitationProbability = hours[0].PrecipitationProbability
            };
        }

        IReadOnlyList<WeatherMinute> nowcast = ParseNowcast(root);
        ForecastConfidence confidence = CalculateConfidence(
            firstFutureHourIndex,
            modelTemps,
            modelRainChance);

        JsonElement daily = root.GetProperty("daily");
        string[] dayTimes = ReadStringArray(daily, "time");
        double[] dayMax = ReadModelDoubleArray(daily, "temperature_2m_max", BestMatch);
        double[] dayMin = ReadModelDoubleArray(daily, "temperature_2m_min", BestMatch);
        int[] dayRain = ReadModelIntArray(daily, "precipitation_probability_max", BestMatch);
        double[] dayRainAmount = ReadModelDoubleArray(daily, "precipitation_sum", BestMatch);
        double[] dayWind = ReadModelDoubleArray(daily, "wind_speed_10m_max", BestMatch);
        double[] dayGust = ReadModelDoubleArray(daily, "wind_gusts_10m_max", BestMatch);
        double[] dayUv = ReadModelDoubleArray(daily, "uv_index_max", BestMatch);
        int[] dayCodes = ReadModelIntArray(daily, "weather_code", BestMatch);
        string[] sunrises = ReadModelStringArray(daily, "sunrise", BestMatch);
        string[] sunsets = ReadModelStringArray(daily, "sunset", BestMatch);
        double[][] modelDayMax =
        [
            dayMax,
            ReadModelDoubleArray(daily, "temperature_2m_max", Ecmwf),
            ReadModelDoubleArray(daily, "temperature_2m_max", IconEurope)
        ];
        int[][] modelDayRain =
        [
            dayRain,
            ReadModelIntArray(daily, "precipitation_probability_max", Ecmwf),
            ReadModelIntArray(daily, "precipitation_probability_max", IconEurope)
        ];
        count = MinLength(dayTimes.Length, dayMax.Length, dayMin.Length, dayRain.Length,
            dayRainAmount.Length, dayWind.Length, dayGust.Length, dayUv.Length,
            dayCodes.Length, sunrises.Length, sunsets.Length);
        var days = new List<WeatherDay>(count);
        for (var index = 0; index < count; index++)
        {
            if (!DateTime.TryParse(dayTimes[index], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out DateTime date) ||
                !HasDailyValues(index, dayMax, dayMin, dayRain, dayRainAmount,
                    dayWind, dayGust, dayUv, dayCodes))
            {
                continue;
            }

            _ = DateTime.TryParse(sunrises[index], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime sunrise);
            _ = DateTime.TryParse(sunsets[index], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime sunset);
            days.Add(new WeatherDay(date, dayMin[index], dayMax[index], dayRain[index],
                dayRainAmount[index], dayWind[index], dayGust[index], dayUv[index],
                ConfidenceScore(index, modelDayMax, modelDayRain), dayCodes[index],
                sunrise, sunset));
        }

        return new ForecastResult(currentResult, confidence, nowcast, hours, days);
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
            ReadDouble(best, "ffmax_val_kmh") ?? ReadDouble(best, "ff_val_kmh") ?? 0,
            ReadText(best, "dd_shortText") ?? ReadText(best, "dd_icon") ?? string.Empty,
            ReadDouble(best, "msl") ?? ReadDouble(best, "p"),
            ReadDouble(best, "td"),
            ReadDouble(best, "rr_val"),
            HaversineKilometres(
                location.Latitude,
                location.Longitude,
                ReadDouble(best, "domain_lat") ?? location.Latitude,
                ReadDouble(best, "domain_lon") ?? location.Longitude),
            observedAt);
    }

    private async Task<OfficialWeatherOutlook?> FetchOfficialOutlookAsync(
        WeatherLocation location,
        CancellationToken cancellationToken)
    {
        if (!IsSlovenian(location) || OfficialRegion(location) is not { } region)
        {
            return null;
        }

        string url = $"{ArsoBase}/fproduct/text/sl/forecast_{region.Code}_latest.xml";
        string xml = await _client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseOfficialOutlook(xml, region.Label);
    }

    internal static OfficialWeatherOutlook? ParseOfficialOutlook(string xml, string region)
    {
        XDocument document = XDocument.Parse(xml);
        XElement? data = document.Descendants("metData").FirstOrDefault();
        if (data is null)
        {
            return null;
        }

        string condition = ReadText(data, "wwsyn_shortText")
            ?? ReadText(data, "nn_shortText")
            ?? "Official regional forecast";
        double? minimum = ReadDouble(data, "tnsyn");
        double? maximum = ReadDouble(data, "txsyn");
        double? gust = ReadDouble(data, "ffmax_val_kmh");
        var details = new List<string> { Capitalize(condition) };
        if (minimum is { } low && maximum is { } high)
        {
            details.Add($"{low:0}–{high:0}°");
        }

        if (gust is > 0)
        {
            details.Add($"gusts {gust:0} km/h");
        }

        return new OfficialWeatherOutlook(
            region,
            string.Join(" · ", details),
            ParseOffset(ReadText(data, "tsUpdated_RFC822"))
            ?? ParseOffset(ReadText(data, "tsValid_issued_RFC822")));
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
            exception is HttpRequestException or TaskCanceledException or JsonException or
                System.Xml.XmlException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static List<WeatherMinute> ParseNowcast(JsonElement root)
    {
        if (!root.TryGetProperty("minutely_15", out JsonElement minutely))
        {
            return [];
        }

        string[] times = ReadStringArray(minutely, "time");
        double[] precipitation = ReadModelDoubleArray(minutely, "precipitation", BestMatch);
        int[] probability = ReadModelIntArray(minutely, "precipitation_probability", BestMatch);
        int[] codes = ReadModelIntArray(minutely, "weather_code", BestMatch);
        double[][] modelPrecipitation =
        [
            precipitation,
            ReadModelDoubleArray(minutely, "precipitation", Ecmwf),
            ReadModelDoubleArray(minutely, "precipitation", IconEurope)
        ];
        int[][] modelProbability =
        [
            probability,
            ReadModelIntArray(minutely, "precipitation_probability", Ecmwf),
            ReadModelIntArray(minutely, "precipitation_probability", IconEurope)
        ];
        int count = MinLength(times.Length, precipitation.Length, probability.Length, codes.Length);
        var result = new List<WeatherMinute>(Math.Min(16, count));
        DateTime now = DateTime.Now.AddMinutes(-8);
        for (var index = 0; index < count && result.Count < 16; index++)
        {
            if (!DateTime.TryParse(times[index], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out DateTime time) || time < now ||
                !double.IsFinite(precipitation[index]) ||
                probability[index] == int.MinValue || codes[index] == int.MinValue)
            {
                continue;
            }

            result.Add(new WeatherMinute(
                time,
                precipitation[index],
                probability[index],
                ConfidenceScore(index, modelPrecipitation, modelProbability),
                codes[index]));
        }

        return result;
    }

    private static ForecastConfidence CalculateConfidence(
        int firstIndex,
        double[][] temperatures,
        int[][] precipitationProbability)
    {
        var temperatureSpreads = new List<double>(12);
        var precipitationSpreads = new List<double>(12);
        for (int index = firstIndex; index < firstIndex + 12; index++)
        {
            if (SpreadAt(index, temperatures) is not { } temperatureSpread ||
                SpreadAt(index, precipitationProbability) is not { } precipitationSpread)
            {
                continue;
            }

            temperatureSpreads.Add(temperatureSpread);
            precipitationSpreads.Add(precipitationSpread);
        }

        double averageTemperatureSpread = temperatureSpreads.Count == 0
            ? 0
            : temperatureSpreads.Average();
        double averagePrecipitationSpread = precipitationSpreads.Count == 0
            ? 0
            : precipitationSpreads.Average();
        int score = ScoreForSpread(averageTemperatureSpread, averagePrecipitationSpread);
        return new ForecastConfidence(
            score,
            averageTemperatureSpread,
            (int)Math.Round(averagePrecipitationSpread),
            Math.Min(temperatures.Length, precipitationProbability.Length));
    }

    private static int ConfidenceScore(
        int index,
        double[][] values,
        int[][] precipitationProbability)
    {
        double valueSpread = SpreadAt(index, values) ?? 0;
        double probabilitySpread = SpreadAt(index, precipitationProbability) ?? 0;
        return ScoreForSpread(valueSpread, probabilitySpread);
    }

    private static int ScoreForSpread(double valueSpread, double probabilitySpread) =>
        (int)Math.Round(Math.Clamp(100 - (valueSpread * 10) - (probabilitySpread * 0.35), 20, 100));

    private static double? SpreadAt(int index, IEnumerable<double[]> series)
    {
        double[] values = series
            .Where(items => index >= 0 && index < items.Length && double.IsFinite(items[index]))
            .Select(items => items[index])
            .ToArray();
        return values.Length < 2 ? null : values.Max() - values.Min();
    }

    private static double? SpreadAt(int index, IEnumerable<int[]> series)
    {
        int[] values = series
            .Where(items => index >= 0 && index < items.Length && items[index] != int.MinValue)
            .Select(items => items[index])
            .ToArray();
        return values.Length < 2 ? null : values.Max() - values.Min();
    }

    private static bool HasHourlyValues(
        int index,
        double[] temperature,
        double[] feelsLike,
        int[] humidity,
        double[] dewPoint,
        int[] rainChance,
        double[] rain,
        int[] codes,
        int[] cloud,
        double[] visibility,
        double[] wind,
        double[] gust) =>
        double.IsFinite(temperature[index]) &&
        double.IsFinite(feelsLike[index]) &&
        humidity[index] != int.MinValue &&
        double.IsFinite(dewPoint[index]) &&
        rainChance[index] != int.MinValue &&
        double.IsFinite(rain[index]) &&
        codes[index] != int.MinValue &&
        cloud[index] != int.MinValue &&
        double.IsFinite(visibility[index]) &&
        double.IsFinite(wind[index]) &&
        double.IsFinite(gust[index]);

    private static bool HasDailyValues(
        int index,
        double[] maximum,
        double[] minimum,
        int[] rainChance,
        double[] rain,
        double[] wind,
        double[] gust,
        double[] uv,
        int[] codes) =>
        double.IsFinite(maximum[index]) &&
        double.IsFinite(minimum[index]) &&
        rainChance[index] != int.MinValue &&
        double.IsFinite(rain[index]) &&
        double.IsFinite(wind[index]) &&
        double.IsFinite(gust[index]) &&
        double.IsFinite(uv[index]) &&
        codes[index] != int.MinValue;

    private void PublishStaleFallback(
        WeatherLocation location,
        ArsoObservation? observation,
        AirQualitySnapshot? air,
        WeatherAlert? warning,
        OfficialWeatherOutlook? outlook)
    {
        if (Current is not { } previous || !previous.Location.Equals(location))
        {
            return;
        }

        WeatherSnapshot stale = previous with
        {
            ObservationTime = observation?.ObservedAt ?? previous.ObservationTime,
            ObservationSource = observation is null
                ? previous.ObservationSource
                : "ARSO live station · cached forecast",
            StationName = observation is null
                ? previous.StationName
                : $"{observation.StationName} · {observation.DistanceKilometres:0.0} km",
            TemperatureCelsius = observation?.TemperatureCelsius ?? previous.TemperatureCelsius,
            RelativeHumidity = observation?.RelativeHumidity ?? previous.RelativeHumidity,
            WindKilometresPerHour = observation?.WindKilometresPerHour ?? previous.WindKilometresPerHour,
            WindGustKilometresPerHour = observation?.WindGustKilometresPerHour ?? previous.WindGustKilometresPerHour,
            WindDirection = observation?.WindDirection ?? previous.WindDirection,
            PressureHectopascals = observation?.PressureHectopascals ?? previous.PressureHectopascals,
            DewPointCelsius = observation?.DewPointCelsius ?? previous.DewPointCelsius,
            PrecipitationMillimetres = observation?.PrecipitationMillimetres ?? previous.PrecipitationMillimetres,
            AirQuality = air ?? previous.AirQuality,
            Alert = warning ?? previous.Alert,
            OfficialOutlook = outlook ?? previous.OfficialOutlook,
            IsStale = true
        };
        Current = stale;
        SnapshotAvailable?.Invoke(this, stale);
    }

    private WeatherSnapshot? LoadCached(WeatherLocation location)
    {
        try
        {
            if (!File.Exists(_cachePath) ||
                DateTime.UtcNow - File.GetLastWriteTimeUtc(_cachePath) > WeatherCacheMaximumAge)
            {
                return null;
            }

            WeatherSnapshot? snapshot = JsonSerializer.Deserialize<WeatherSnapshot>(
                File.ReadAllText(_cachePath));
            return snapshot is not null &&
                   HaversineKilometres(
                       snapshot.Location.Latitude,
                       snapshot.Location.Longitude,
                       location.Latitude,
                       location.Longitude) <= 5
                ? snapshot with { Location = location, IsStale = true }
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private void SaveCached(WeatherSnapshot snapshot)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_cachePath);
            if (directory is null)
            {
                return;
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = _cachePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot));
            File.Move(temporaryPath, _cachePath, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
        }
    }

    private static double CalculateApparentTemperature(
        double temperatureCelsius,
        int relativeHumidity,
        double windKilometresPerHour)
    {
        double vapourPressure = Math.Clamp(relativeHumidity, 0, 100) / 100d *
            6.105 * Math.Exp(17.27 * temperatureCelsius / (237.7 + temperatureCelsius));
        double apparent = temperatureCelsius + (0.33 * vapourPressure) -
            (0.7 * Math.Max(0, windKilometresPerHour) / 3.6) - 4;
        return Math.Clamp(apparent, -80, 60);
    }

    private static bool TryParseCoordinates(string query, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        string[] parts = query.Trim().Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        bool parsed = parts.Length == 2 &&
                      double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) &&
                      double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) &&
                      latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
        return parsed;
    }

    private static (string Code, string Label)? OfficialRegion(WeatherLocation location)
    {
        if (IsCelje(location.Name, location.Latitude, location.Longitude))
        {
            return ("SI_SAVINJSKA", "Savinjska");
        }

        return WarningRegion(location) switch
        {
            "SLOVENIA_NORTH-WEST" => ("SI_GORENJSKA", "Gorenjska"),
            "SLOVENIA_SOUTH-WEST" => ("SI_NOTRANJSKO-KRASKA", "Notranjska"),
            "SLOVENIA_SOUTH-EAST" => ("SI_DOLENJSKA", "Dolenjska"),
            "SLOVENIA_NORTH-EAST" => ("SI_PODRAVSKA", "Podravje"),
            _ => ("SI_OSREDNJESLOVENSKA", "Central Slovenia")
        };
    }

    private static string Capitalize(string value) => string.IsNullOrWhiteSpace(value)
        ? value
        : char.ToUpper(value[0], CultureInfo.CurrentCulture) + value[1..];

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
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double result) &&
        double.IsFinite(result) ? result : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int result)
            ? result
            : GetDouble(element, name) is { } number ? (int)Math.Round(number) : null;

    private static string[] ReadStringArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement values)
            ? values.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray()
            : [];

    private static string[] ReadModelStringArray(JsonElement parent, string name, string model) =>
        ReadStringArray(parent, $"{name}_{model}");

    internal static double[] ReadDoubleArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement values)
            ? values.EnumerateArray().Select(value =>
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out double number) && double.IsFinite(number)
                    ? number
                    : double.NaN).ToArray()
            : [];

    private static double[] ReadModelDoubleArray(JsonElement parent, string name, string model) =>
        ReadDoubleArray(parent, $"{name}_{model}");

    internal static int[] ReadIntArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement values)
            ? values.EnumerateArray().Select(value =>
                value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
                    ? number
                    : int.MinValue).ToArray()
            : [];

    private static int[] ReadModelIntArray(JsonElement parent, string name, string model) =>
        ReadIntArray(parent, $"{name}_{model}");

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
        double WindGustKilometresPerHour,
        string WindDirection,
        double? PressureHectopascals,
        double? DewPointCelsius,
        double? PrecipitationMillimetres,
        double DistanceKilometres,
        DateTimeOffset? ObservedAt);

    private sealed record CurrentForecast(
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
        int WeatherCode);

    private sealed record ForecastResult(
        CurrentForecast Current,
        ForecastConfidence Confidence,
        IReadOnlyList<WeatherMinute> Nowcast,
        IReadOnlyList<WeatherHour> Hourly,
        IReadOnlyList<WeatherDay> Daily);
}
