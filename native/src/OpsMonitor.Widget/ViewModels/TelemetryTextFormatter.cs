using System.Globalization;

namespace OpsMonitor.Widget.ViewModels;

internal static class TelemetryTextFormatter
{
    internal const string Unavailable = "N/A";

    public static string Percentage(double value, int? decimals = null)
        => $"{Number(value, decimals ?? 0)}%";

    public static string Temperature(double? value, int? decimals = null)
        => value is { } temperature
            ? $"{Number(temperature, decimals ?? 0)}°C"
            : Unavailable;

    public static string Memory(double usedGigabytes, double totalGigabytes, int? decimals = null)
    {
        var precision = decimals ?? 1;
        return $"{Number(usedGigabytes, precision)} / {Number(totalGigabytes, precision)} GB";
    }

    public static string Memory(
        double? usedGigabytes,
        double? totalGigabytes,
        int? decimals = null)
    {
        var precision = decimals ?? 1;
        var used = usedGigabytes is { } usedValue
            ? Number(usedValue, precision)
            : Unavailable;
        var total = totalGigabytes is { } totalValue
            ? Number(totalValue, precision)
            : Unavailable;
        return $"{used} / {total} GB";
    }

    public static string NetworkThroughput(
        double? downloadBytesPerSecond,
        double? uploadBytesPerSecond,
        int? decimals = null)
        => $"↓{TightRate(downloadBytesPerSecond, decimals)}  ↑{TightRate(uploadBytesPerSecond, decimals)}";

    public static string Latency(double milliseconds, int? decimals = null)
        => $"{Number(milliseconds, decimals ?? 0)} ms";

    public static string PacketLoss(double percent, int? decimals = null)
        => $"{Number(percent, decimals ?? 1)}%";

    public static string Rate(double bytesPerSecond, int? decimals = null)
    {
        var absolute = Math.Abs(bytesPerSecond);
        return absolute switch
        {
            >= 1_000_000_000 =>
                $"{Number(bytesPerSecond / 1_000_000_000, decimals ?? 1)} GB/s",
            >= 1_000_000 =>
                $"{Number(bytesPerSecond / 1_000_000, decimals ?? 1)} MB/s",
            >= 1_000 =>
                $"{Number(bytesPerSecond / 1_000, decimals ?? 0)} KB/s",
            _ => $"{Number(bytesPerSecond, decimals ?? 0)} B/s"
        };
    }

    public static string Rate(double? bytesPerSecond, int? decimals = null)
        => bytesPerSecond is { } value
            ? Rate(value, decimals)
            : Unavailable;

    public static string ByteSize(double bytes, int? decimals = null)
    {
        double absolute = Math.Abs(bytes);
        return absolute switch
        {
            >= 1024d * 1024d * 1024d =>
                $"{Number(bytes / (1024d * 1024d * 1024d), decimals ?? 1)} GB",
            >= 1024d * 1024d =>
                $"{Number(bytes / (1024d * 1024d), decimals ?? 1)} MB",
            >= 1024d => $"{Number(bytes / 1024d, decimals ?? 0)} KB",
            _ => $"{Number(bytes, decimals ?? 0)} B"
        };
    }

    public static string CompactRate(double bytesPerSecond)
    {
        var absolute = Math.Abs(bytesPerSecond);
        return absolute switch
        {
            >= 1_000_000_000 =>
                $"{Number(bytesPerSecond / 1_000_000_000, 1)} GB/s",
            >= 1_000_000 =>
                $"{Number(bytesPerSecond / 1_000_000, 1)} MB/s",
            >= 1_000 =>
                $"{Number(bytesPerSecond / 1_000, 0)} KB/s",
            _ => $"{Number(bytesPerSecond, 0)} B/s"
        };
    }

    private static string TightRate(double? bytesPerSecond, int? decimals = null)
    {
        if (bytesPerSecond is not { } value)
        {
            return Unavailable;
        }

        var absolute = Math.Abs(value);
        return absolute switch
        {
            >= 1_000_000_000 =>
                $"{Number(value / 1_000_000_000, decimals ?? 1)}G/s",
            >= 1_000_000 =>
                $"{Number(value / 1_000_000, decimals ?? 1)}M/s",
            >= 1_000 =>
                $"{Number(value / 1_000, decimals ?? 0)}K/s",
            _ => $"{Number(value, decimals ?? 0)}B/s"
        };
    }

    public static string Duration(TimeSpan? value)
        => value is { } duration
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)duration.TotalHours}h {duration.Minutes:00}m")
            : Unavailable;

    public static string Number(double value, int decimals)
    {
        decimals = Math.Clamp(decimals, 0, 3);
        return value.ToString(
            decimals == 0 ? "0" : $"0.{new string('0', decimals)}",
            CultureInfo.InvariantCulture);
    }
}
