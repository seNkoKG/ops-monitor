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

internal enum SensorBridgeFailureSource
{
    Probe,
    Publication
}

internal sealed class ProbeRecoveryPolicy
{
    internal const int UnavailablePollLimit = 3;
    internal static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    private int _unavailablePolls;
    private int _recoveryAttempts;

    internal DateTimeOffset NextAttemptUtc { get; private set; } =
        DateTimeOffset.MinValue;

    internal int RecoveryAttempts => _recoveryAttempts;

    internal bool CanAttempt(DateTimeOffset timestampUtc) =>
        timestampUtc >= NextAttemptUtc;

    internal bool RecordUnavailable(DateTimeOffset timestampUtc)
    {
        _unavailablePolls++;
        if (_unavailablePolls < UnavailablePollLimit)
        {
            return false;
        }

        _unavailablePolls = 0;
        ScheduleNextAttempt(timestampUtc);
        return true;
    }

    internal bool RecordFailure(
        SensorBridgeFailureSource source,
        DateTimeOffset timestampUtc)
    {
        if (source == SensorBridgeFailureSource.Publication)
        {
            return false;
        }

        if (source != SensorBridgeFailureSource.Probe)
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, null);
        }

        _unavailablePolls = 0;
        ScheduleNextAttempt(timestampUtc);
        return true;
    }

    internal void RecordAvailable()
    {
        _unavailablePolls = 0;
        _recoveryAttempts = 0;
        NextAttemptUtc = DateTimeOffset.MinValue;
    }

    internal CpuTemperatureProbeResult CreateWaitingResult(
        DateTimeOffset timestampUtc) =>
        new()
        {
            TimestampUtc = timestampUtc,
            Message = string.Create(
                CultureInfo.InvariantCulture,
                $"CPU sensor recovery is backing off until {NextAttemptUtc:O}.")
        };

    private void ScheduleNextAttempt(DateTimeOffset timestampUtc)
    {
        NextAttemptUtc = timestampUtc + GetDelay(_recoveryAttempts);
        _recoveryAttempts++;
    }

    internal static TimeSpan GetDelay(int recoveryAttempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recoveryAttempt);
        int exponent = Math.Min(recoveryAttempt, 4);
        double milliseconds =
            InitialDelay.TotalMilliseconds * (1 << exponent);
        return TimeSpan.FromMilliseconds(
            Math.Min(milliseconds, MaximumDelay.TotalMilliseconds));
    }
}

internal sealed class SensorBridgeHost
{
    private const int MissingWidgetPollLimit = 4;
    private static readonly TimeSpan CatalogPublicationCadence =
        TimeSpan.FromSeconds(6);
    private readonly SensorBridgeOptions _options;

    public SensorBridgeHost(SensorBridgeOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        LibreHardwareMonitorCpuProbe? probe = null;
        string? lastDiagnosticState = null;
        int missingWidgetPolls = 0;
        DateTimeOffset lastCatalogPublicationUtc = DateTimeOffset.MinValue;
        var recovery = new ProbeRecoveryPolicy();

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DateTimeOffset timestampUtc = DateTimeOffset.UtcNow;
                HardwareProbeResult? snapshot = null;
                CpuTemperatureProbeResult? result = null;
                SensorBridgeFailureSource? cycleFailure = null;

                if (!recovery.CanAttempt(timestampUtc))
                {
                    result = recovery.CreateWaitingResult(timestampUtc);
                }
                else
                {
                    try
                    {
                        probe ??= new LibreHardwareMonitorCpuProbe();
                        snapshot = probe.ReadSnapshot(timestampUtc);
                        result = snapshot.CpuTemperature;

                        if (result.IsAvailable)
                        {
                            recovery.RecordAvailable();
                        }
                        else if (recovery.RecordUnavailable(timestampUtc))
                        {
                            probe.Dispose();
                            probe = null;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception) when (SensorFailure.IsExpected(exception))
                    {
                        cycleFailure = SensorBridgeFailureSource.Probe;
                        if (recovery.RecordFailure(
                                SensorBridgeFailureSource.Probe,
                                timestampUtc))
                        {
                            probe?.Dispose();
                            probe = null;
                        }

                        string diagnosticState =
                            $"probe-fault|{exception.GetType().FullName}|{exception.Message}";
                        if (!StringComparer.Ordinal.Equals(
                                lastDiagnosticState,
                                diagnosticState))
                        {
                            await SensorBridgeDiagnostics.TryWriteAsync(
                                _options.DiagnosticPath,
                                SensorBridgeDiagnostics.FormatProbeFault(
                                    exception,
                                    recovery.NextAttemptUtc),
                                cancellationToken).ConfigureAwait(false);
                            lastDiagnosticState = diagnosticState;
                        }
                    }
                }

                if (cycleFailure is null && result is not null)
                {
                    if (result.IsAvailable)
                    {
                        try
                        {
                            await AtomicTextFile.WriteAsync(
                                _options.OutputPath,
                                FormatPayload(result),
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception) when (
                            SensorFailure.IsExpected(exception))
                        {
                            cycleFailure = SensorBridgeFailureSource.Publication;
                            _ = recovery.RecordFailure(
                                SensorBridgeFailureSource.Publication,
                                timestampUtc);
                            string diagnosticState =
                                $"publication-fault|{exception.GetType().FullName}|" +
                                exception.Message;
                            if (!StringComparer.Ordinal.Equals(
                                    lastDiagnosticState,
                                    diagnosticState))
                            {
                                await SensorBridgeDiagnostics.TryWriteAsync(
                                    _options.DiagnosticPath,
                                    SensorBridgeDiagnostics.FormatPublicationFault(
                                        exception,
                                        _options.OutputPath),
                                    cancellationToken).ConfigureAwait(false);
                                lastDiagnosticState = diagnosticState;
                            }
                        }
                    }

                    if (cycleFailure is null)
                    {
                        if (snapshot?.HasSensors == true &&
                            (_options.Once ||
                             timestampUtc - lastCatalogPublicationUtc >=
                             CatalogPublicationCadence))
                        {
                            try
                            {
                                await AtomicTextFile.WriteAsync(
                                    _options.CatalogPath,
                                    FormatCatalogPayload(snapshot),
                                    cancellationToken).ConfigureAwait(false);
                                lastCatalogPublicationUtc = timestampUtc;
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception exception) when (
                                SensorFailure.IsExpected(exception))
                            {
                                cycleFailure = SensorBridgeFailureSource.Publication;
                                await SensorBridgeDiagnostics.TryWriteAsync(
                                    _options.DiagnosticPath,
                                    SensorBridgeDiagnostics.FormatPublicationFault(
                                        exception,
                                        _options.CatalogPath),
                                    cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }

                    if (cycleFailure is null)
                    {
                        string diagnosticState =
                            SensorBridgeDiagnostics.GetStateKey(result);
                        if (!StringComparer.Ordinal.Equals(
                                lastDiagnosticState,
                                diagnosticState))
                        {
                            await SensorBridgeDiagnostics.TryWriteAsync(
                                _options.DiagnosticPath,
                                SensorBridgeDiagnostics.Format(result),
                                cancellationToken).ConfigureAwait(false);
                            lastDiagnosticState = diagnosticState;
                        }
                    }
                }

                if (_options.Once)
                {
                    if (cycleFailure is not null)
                    {
                        return SensorBridgeExitCodes.Faulted;
                    }

                    return result?.IsAvailable == true
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
        finally
        {
            probe?.Dispose();
        }
    }

    internal static bool ShouldExitAfterDelay(
        int missingWidgetPolls,
        bool stayAlive,
        bool widgetIsRunning) =>
        !stayAlive &&
        missingWidgetPolls >= MissingWidgetPollLimit &&
        !widgetIsRunning;

    internal static bool ShouldResetProbeAfterUnavailablePolls(int unavailablePolls) =>
        unavailablePolls >= ProbeRecoveryPolicy.UnavailablePollLimit;

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

    internal static string FormatCatalogPayload(HardwareProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.HasSensors)
        {
            throw new ArgumentException(
                "Only a non-empty hardware sensor snapshot can be published.",
                nameof(result));
        }

        return result.ToJson();
    }

    private static bool IsWidgetRunning()
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName("OpsMonitor.Widget");
            return processes.Length > 0;
        }
        catch (Exception exception) when (SensorFailure.IsExpected(exception))
        {
            // Failure to inspect the user session must keep the bridge alive.
            // Exiting here would turn a transient process-enumeration fault
            // into a permanent loss of temperature telemetry.
            return true;
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

    internal static string FormatPublicationFault(
        Exception exception,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return string.Join(
            Environment.NewLine,
            "Temperature publication fault; retrying.",
            $"Type={exception.GetType().FullName}",
            $"OutputPath={outputPath}",
            $"Message={exception.Message}");
    }

    internal static string FormatProbeFault(
        Exception exception,
        DateTimeOffset nextAttemptUtc)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return string.Join(
            Environment.NewLine,
            "CPU sensor probe fault; retrying.",
            $"Type={exception.GetType().FullName}",
            $"NextAttemptUtc={nextAttemptUtc:O}",
            $"Message={exception.Message}");
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
