using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OpsMonitor.SensorBridge;

internal static class SensorBridgeExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 2;
    public const int SensorUnavailable = 3;
    public const int Faulted = 4;
    public const int AlreadyRunning = 5;
}

internal sealed class SensorBridgeHost
{
    private const int MissingWidgetPollLimit = 4;
    private readonly SensorBridgeOptions _options;

    public SensorBridgeHost(SensorBridgeOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var probe = new LibreHardwareMonitorCpuProbe();
        string? lastDiagnosticState = null;
        int missingWidgetPolls = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CpuTemperatureProbeResult result = probe.Read(DateTimeOffset.UtcNow);
            string diagnostic = SensorBridgeDiagnostics.Format(result);
            string diagnosticState = SensorBridgeDiagnostics.GetStateKey(result);

            if (result.IsAvailable)
            {
                string payload = FormatPayload(result);
                await AtomicTextFile.WriteAsync(
                    _options.OutputPath,
                    payload,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!StringComparer.Ordinal.Equals(lastDiagnosticState, diagnosticState))
            {
                await SensorBridgeDiagnostics.TryWriteAsync(
                    _options.DiagnosticPath,
                    diagnostic,
                    cancellationToken).ConfigureAwait(false);
                lastDiagnosticState = diagnosticState;
            }

            if (_options.Once)
            {
                return result.IsAvailable
                    ? SensorBridgeExitCodes.Success
                    : SensorBridgeExitCodes.SensorUnavailable;
            }

            bool widgetIsRunning = _options.StayAlive || IsWidgetRunning();
            missingWidgetPolls = widgetIsRunning ? 0 : missingWidgetPolls + 1;

            await Task.Delay(_options.Interval, cancellationToken).ConfigureAwait(false);
            bool widgetReturnedDuringDelay = _options.StayAlive || IsWidgetRunning();
            if (ShouldExitAfterDelay(
                    missingWidgetPolls,
                    _options.StayAlive,
                    widgetReturnedDuringDelay))
            {
                return SensorBridgeExitCodes.Success;
            }
        }
    }

    internal static bool ShouldExitAfterDelay(
        int missingWidgetPolls,
        bool stayAlive,
        bool widgetIsRunning) =>
        !stayAlive &&
        missingWidgetPolls >= MissingWidgetPollLimit &&
        !widgetIsRunning;

    internal static string FormatPayload(CpuTemperatureProbeResult result)
    {
        if (!result.IsAvailable || result.TemperatureCelsius is not double temperature)
        {
            throw new ArgumentException(
                "Only an available temperature can be published.",
                nameof(result));
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{temperature:0.###}|{result.TimestampUtc.UtcDateTime.Ticks}");
    }

    private static bool IsWidgetRunning()
    {
        Process[] processes = Process.GetProcessesByName("OpsMonitor.Widget");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }
}

internal static class SensorBridgeDiagnostics
{
    internal static string GetStateKey(CpuTemperatureProbeResult result) =>
        string.Join(
            '|',
            result.IsAvailable,
            result.HardwareIdentifier,
            result.SensorIdentifier,
            result.Message);

    internal static string Format(CpuTemperatureProbeResult result)
    {
        var text = new StringBuilder();
        text.AppendLine(result.IsAvailable ? "Available" : "Unavailable");
        text.Append("TimestampUtc=").AppendLine(
            result.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));

        if (result.TemperatureCelsius is double temperature)
        {
            text.Append("TemperatureCelsius=").AppendLine(
                temperature.ToString("0.###", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(result.HardwareName))
        {
            text.Append("Hardware=").AppendLine(result.HardwareName);
        }

        if (!string.IsNullOrWhiteSpace(result.SensorName))
        {
            text.Append("Sensor=").AppendLine(result.SensorName);
        }

        if (!string.IsNullOrWhiteSpace(result.SensorIdentifier))
        {
            text.Append("Identifier=").AppendLine(result.SensorIdentifier);
        }

        text.Append("Status=").Append(result.Message);
        return text.ToString();
    }

    internal static async Task TryWriteAsync(
        string path,
        string contents,
        CancellationToken cancellationToken)
    {
        try
        {
            await AtomicTextFile.WriteAsync(path, contents, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (SensorFailure.IsExpected(exception))
        {
            // Diagnostics must never stop temperature publication.
        }
    }
}
