using System.Globalization;
using System.Text;
using OpsMonitor.Widget.Models;

namespace OpsMonitor.Widget.Services;

internal static class ArsoRadarParser
{
    private const double Wgs84CenterLatitude = 46.065727;
    private const double Wgs84CenterLongitude = 14.758668;

    internal static RadarRainObservation? Parse(byte[] bytes, WeatherLocation location)
    {
        if (bytes.Length < 1024 || FindDataStart(bytes) is not { } dataStart)
        {
            return null;
        }

        string headerText = Encoding.ASCII.GetString(bytes, 0, dataStart);
        Dictionary<string, string> header = headerText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line[0] != '#')
            .Select(line => line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First()[1],
                StringComparer.OrdinalIgnoreCase);

        if (!HeaderEquals(header, "domain", "SI0") ||
            !HeaderEquals(header, "proj", "LCC") ||
            !HeaderEquals(header, "quant", "RRG") ||
            !TryHeaderNumbers(header, "ncell", out double[] cells) ||
            cells.Length < 2 ||
            !TryHeaderNumbers(header, "cellsize", out double[] cellSize) ||
            cellSize.Length < 2 ||
            !TryHeaderNumbers(header, "offset", out double[] offsets) ||
            !TryHeaderNumbers(header, "nlevel", out double[] levelCounts) ||
            !TryHeaderNumbers(header, "nodata", out double[] noDataValues) ||
            !TryHeaderNumbers(header, "start", out double[] starts) ||
            !TryHeaderNumbers(header, "slope", out double[] slopes) ||
            !TryRadarTime(header, out DateTimeOffset observedAt))
        {
            return null;
        }

        int width = (int)Math.Round(cells[0]);
        int height = (int)Math.Round(cells[1]);
        double cellWidth = cellSize[0];
        double cellHeight = cellSize[1];
        int offset = (int)Math.Round(offsets[0]);
        int levelCount = (int)Math.Round(levelCounts[0]);
        int noData = (int)Math.Round(noDataValues[0]);
        double start = starts[0];
        double slope = slopes[0];
        if (width < 3 || height < 3 || width > 2000 || height > 2000 ||
            cellWidth <= 0 || cellHeight <= 0 ||
            offset is < 0 or > 255 || levelCount is < 2 or > 128 ||
            noData is < 0 or > 255 || !double.IsFinite(start) || !double.IsFinite(slope))
        {
            return null;
        }

        int newlineLength = dataStart + width < bytes.Length && bytes[dataStart + width] == (byte)'\r'
            ? 2
            : 1;
        int rowStride = width + newlineLength;
        if (dataStart + (height * rowStride) > bytes.Length + newlineLength)
        {
            return null;
        }

        (double x, double y) = ProjectToKilometres(location.Latitude, location.Longitude);
        int column = (int)Math.Round((width - 1) / 2d + (x / cellWidth));
        int row = (int)Math.Round((height - 1) / 2d - (y / cellHeight));
        if (column < 0 || column >= width || row < 0 || row >= height)
        {
            return null;
        }

        var innerRain = new List<double>(9);
        var localRain = new List<double>(81);
        var localValid = new List<double>(81);
        for (int rowOffset = -4; rowOffset <= 4; rowOffset++)
        {
            for (int columnOffset = -4; columnOffset <= 4; columnOffset++)
            {
                int sampleRow = row + rowOffset;
                int sampleColumn = column + columnOffset;
                if (sampleRow < 0 || sampleRow >= height ||
                    sampleColumn < 0 || sampleColumn >= width ||
                    TryReadRate(
                        bytes,
                        dataStart + (sampleRow * rowStride) + sampleColumn,
                        offset,
                        levelCount,
                        noData,
                        start,
                        slope) is not { } rainRate)
                {
                    continue;
                }

                localValid.Add(rainRate);
                if (rainRate >= 0.2)
                {
                    localRain.Add(rainRate);
                    if (Math.Abs(rowOffset) <= 1 && Math.Abs(columnOffset) <= 1)
                    {
                        innerRain.Add(rainRate);
                    }
                }
            }
        }

        if (localValid.Count < 9)
        {
            return null;
        }

        int coverage = (int)Math.Round(100d * localRain.Count / localValid.Count);
        bool isRainDetected = innerRain.Count >= 3 || coverage >= 50;
        List<double> intensitySamples = innerRain.Count >= 3 ? innerRain : localRain;
        double localRate = isRainDetected && intensitySamples.Count > 0
            ? Median(intensitySamples)
            : 0;
        double peakRate = localRain.Count > 0 ? localRain.Max() : 0;
        return new RadarRainObservation(
            observedAt,
            localRate,
            peakRate,
            coverage,
            isRainDetected);
    }

    private static int? FindDataStart(byte[] bytes)
    {
        int maximum = Math.Min(bytes.Length - 4, 16 * 1024);
        for (var index = 0; index < maximum; index++)
        {
            bool startsLine = index == 0 || bytes[index - 1] is (byte)'\n' or (byte)'\r';
            if (!startsLine || bytes[index] != (byte)'D' || bytes[index + 1] != (byte)'A' ||
                bytes[index + 2] != (byte)'T' || bytes[index + 3] != (byte)'A')
            {
                continue;
            }

            int cursor = index + 4;
            if (cursor < bytes.Length && bytes[cursor] == (byte)'\r')
            {
                cursor++;
            }

            return cursor < bytes.Length && bytes[cursor] == (byte)'\n'
                ? cursor + 1
                : null;
        }

        return null;
    }

    private static bool HeaderEquals(
        Dictionary<string, string> header,
        string name,
        string expected) =>
        header.TryGetValue(name, out string? value) &&
        value.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool TryHeaderNumbers(
        Dictionary<string, string> header,
        string name,
        out double[] values)
    {
        values = [];
        if (!header.TryGetValue(name, out string? text))
        {
            return false;
        }

        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        values = new double[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(
                    parts[index],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out values[index]) ||
                !double.IsFinite(values[index]))
            {
                values = [];
                return false;
            }
        }

        return values.Length > 0;
    }

    private static bool TryRadarTime(
        Dictionary<string, string> header,
        out DateTimeOffset observedAt)
    {
        observedAt = default;
        return header.TryGetValue("time", out string? value) &&
               DateTimeOffset.TryParseExact(
                   value.Trim(),
                   "yyyy MM dd HH mm",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out observedAt);
    }

    private static double? TryReadRate(
        byte[] bytes,
        int index,
        int offset,
        int levelCount,
        int noData,
        double start,
        double slope)
    {
        if (index < 0 || index >= bytes.Length)
        {
            return null;
        }

        int level = bytes[index];
        if (level == noData || level < offset || level >= offset + levelCount)
        {
            return null;
        }

        if (level == offset)
        {
            return 0;
        }

        double decibels = start + (slope * (level - offset));
        double rate = Math.Pow(10, decibels / 10);
        return double.IsFinite(rate) ? Math.Clamp(rate, 0, 200) : null;
    }

    private static (double X, double Y) ProjectToKilometres(double latitude, double longitude)
    {
        const double radius = 6371;
        double radians = Math.PI / 180;
        double parallel = Wgs84CenterLatitude * radians;
        double latitudeRadians = latitude * radians;
        double longitudeRadians = longitude * radians;
        double originLongitude = Wgs84CenterLongitude * radians;
        double n = Math.Sin(parallel);
        double f = Math.Cos(parallel) *
                   Math.Pow(Math.Tan((Math.PI / 4) + (parallel / 2)), n) / n;
        double rho = radius * f /
                     Math.Pow(Math.Tan((Math.PI / 4) + (latitudeRadians / 2)), n);
        double rhoOrigin = radius * f /
                           Math.Pow(Math.Tan((Math.PI / 4) + (parallel / 2)), n);
        double theta = n * (longitudeRadians - originLongitude);
        return (rho * Math.Sin(theta), rhoOrigin - (rho * Math.Cos(theta)));
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            return 0;
        }

        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }
}

internal sealed record RadarRainObservation(
    DateTimeOffset ObservedAt,
    double LocalRainRateMillimetresPerHour,
    double PeakRainRateMillimetresPerHour,
    int RainCoveragePercent,
    bool IsRainDetected);
