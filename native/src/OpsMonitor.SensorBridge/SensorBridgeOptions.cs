namespace OpsMonitor.SensorBridge;

internal sealed record SensorBridgeOptions
{
    internal const int MinimumIntervalMilliseconds = 1_000;
    internal const int MaximumIntervalMilliseconds = 60_000;
    internal const int DefaultIntervalMilliseconds = 3_000;

    internal static readonly string DefaultDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PerformancePill");

    internal static readonly string DefaultOutputPath = Path.Combine(
        DefaultDataDirectory,
        "cpu-temperature.txt");

    internal static readonly string DefaultDiagnosticPath = Path.Combine(
        DefaultDataDirectory,
        "cpu-temperature-diagnostic.txt");

    internal static readonly string DefaultCatalogPath = Path.Combine(
        DefaultDataDirectory,
        "hardware-sensors.json");

    public string OutputPath { get; init; } = DefaultOutputPath;
    public string DiagnosticPath { get; init; } = DefaultDiagnosticPath;
    public string CatalogPath { get; init; } = DefaultCatalogPath;
    public TimeSpan Interval { get; init; } =
        TimeSpan.FromMilliseconds(DefaultIntervalMilliseconds);
    public bool Once { get; init; }
    public bool StayAlive { get; init; }

    internal static bool TryParse(
        IReadOnlyList<string> args,
        out SensorBridgeOptions options,
        out string error)
    {
        string outputPath = DefaultOutputPath;
        string diagnosticPath = DefaultDiagnosticPath;
        string? catalogPath = null;
        int intervalMilliseconds = DefaultIntervalMilliseconds;
        bool once = false;
        bool stayAlive = false;

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            switch (argument.ToLowerInvariant())
            {
                case "--once":
                    once = true;
                    break;
                case "--stay-alive":
                    stayAlive = true;
                    break;
                case "--output":
                    if (!TryTakeValue(args, ref index, out outputPath))
                    {
                        options = new SensorBridgeOptions();
                        error = "--output requires a file path.";
                        return false;
                    }

                    break;
                case "--diagnostic":
                    if (!TryTakeValue(args, ref index, out diagnosticPath))
                    {
                        options = new SensorBridgeOptions();
                        error = "--diagnostic requires a file path.";
                        return false;
                    }

                    break;
                case "--catalog":
                    if (!TryTakeValue(args, ref index, out catalogPath))
                    {
                        options = new SensorBridgeOptions();
                        error = "--catalog requires a file path.";
                        return false;
                    }

                    break;
                case "--interval-ms":
                    if (!TryTakeValue(args, ref index, out string intervalText) ||
                        !int.TryParse(intervalText, out intervalMilliseconds) ||
                        intervalMilliseconds is <
                            MinimumIntervalMilliseconds or >
                            MaximumIntervalMilliseconds)
                    {
                        options = new SensorBridgeOptions();
                        error =
                            $"--interval-ms must be between " +
                            $"{MinimumIntervalMilliseconds} and " +
                            $"{MaximumIntervalMilliseconds}.";
                        return false;
                    }

                    break;
                default:
                    options = new SensorBridgeOptions();
                    error = $"Unknown argument: {argument}";
                    return false;
            }
        }

        if (once && stayAlive)
        {
            options = new SensorBridgeOptions();
            error = "--once and --stay-alive cannot be used together.";
            return false;
        }

        try
        {
            outputPath = Path.GetFullPath(outputPath);
            diagnosticPath = Path.GetFullPath(diagnosticPath);
            catalogPath = Path.GetFullPath(
                catalogPath ?? Path.Combine(
                    Path.GetDirectoryName(outputPath) ?? DefaultDataDirectory,
                    "hardware-sensors.json"));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            options = new SensorBridgeOptions();
            error = $"A configured path is invalid: {exception.Message}";
            return false;
        }

        options = new SensorBridgeOptions
        {
            OutputPath = outputPath,
            DiagnosticPath = diagnosticPath,
            CatalogPath = catalogPath,
            Interval = TimeSpan.FromMilliseconds(intervalMilliseconds),
            Once = once,
            StayAlive = stayAlive
        };
        error = string.Empty;
        return true;
    }

    private static bool TryTakeValue(
        IReadOnlyList<string> args,
        ref int index,
        out string value)
    {
        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = string.Empty;
            return false;
        }

        index++;
        value = args[index];
        return true;
    }
}
