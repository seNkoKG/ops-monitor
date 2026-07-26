using LibreHardwareMonitor.Hardware;

namespace OpsMonitor.SensorBridge;

internal sealed record CpuTemperatureSensorCandidate(
    string HardwareName,
    string HardwareIdentifier,
    string SensorName,
    string SensorIdentifier,
    double? TemperatureCelsius);

internal sealed record CpuTemperatureProbeResult
{
    public double? TemperatureCelsius { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public string HardwareName { get; init; } = string.Empty;
    public string HardwareIdentifier { get; init; } = string.Empty;
    public string SensorName { get; init; } = string.Empty;
    public string SensorIdentifier { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool IsAvailable =>
        TemperatureCelsius is double temperature &&
        double.IsFinite(temperature) &&
        temperature is >= LibreHardwareMonitorCpuProbe.MinimumTemperatureCelsius
            and <= LibreHardwareMonitorCpuProbe.MaximumTemperatureCelsius;
}

internal sealed class LibreHardwareMonitorCpuProbe : IDisposable
{
    internal const double MinimumTemperatureCelsius = 5;
    internal const double MaximumTemperatureCelsius = 125;

    private readonly Computer _computer;
    private bool _opened;
    private bool _disposed;

    internal LibreHardwareMonitorCpuProbe()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_opened)
        {
            try
            {
                _computer.Close();
            }
            catch (Exception exception) when (SensorFailure.IsExpected(exception))
            {
                // Computer.Close is LibreHardwareMonitor's disposal contract.
            }
        }
    }

    internal CpuTemperatureProbeResult Read(DateTimeOffset timestampUtc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            EnsureOpen();
            var candidates = new List<CpuTemperatureSensorCandidate>();
            foreach (IHardware hardware in _computer.Hardware.Where(
                         hardware => hardware.HardwareType == HardwareType.Cpu))
            {
                CollectCandidates(
                    hardware,
                    hardware.Name,
                    hardware.Identifier.ToString(),
                    candidates);
            }

            CpuTemperatureSensorCandidate? selected = SelectPreferredSensor(candidates);
            if (selected is null)
            {
                string detail = candidates.Count == 0
                    ? "No CPU temperature sensor was exposed."
                    : "CPU temperature sensors returned no plausible live value.";
                return Unavailable(
                    timestampUtc,
                    $"{detail} Elevated sensor access may be unavailable.");
            }

            return new CpuTemperatureProbeResult
            {
                TemperatureCelsius = selected.TemperatureCelsius,
                TimestampUtc = timestampUtc,
                HardwareName = selected.HardwareName,
                HardwareIdentifier = selected.HardwareIdentifier,
                SensorName = selected.SensorName,
                SensorIdentifier = selected.SensorIdentifier,
                Message = $"{selected.SensorName} is live."
            };
        }
        catch (Exception exception) when (SensorFailure.IsExpected(exception))
        {
            return Unavailable(
                timestampUtc,
                exception is UnauthorizedAccessException
                    ? "CPU sensor access was denied. Run the installed sensor task at highest privileges."
                    : $"CPU sensor probe failed: {exception.Message}");
        }
    }

    internal static CpuTemperatureSensorCandidate? SelectPreferredSensor(
        IEnumerable<CpuTemperatureSensorCandidate> candidates) =>
        candidates
            .Where(candidate =>
                candidate.TemperatureCelsius is double temperature &&
                double.IsFinite(temperature) &&
                temperature is >= MinimumTemperatureCelsius
                    and <= MaximumTemperatureCelsius)
            .OrderBy(GetPreference)
            .ThenBy(candidate => candidate.HardwareIdentifier, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SensorIdentifier, StringComparer.Ordinal)
            .FirstOrDefault();

    private static int GetPreference(CpuTemperatureSensorCandidate candidate)
    {
        bool isAmd =
            candidate.HardwareIdentifier.StartsWith(
                "/amdcpu/",
                StringComparison.OrdinalIgnoreCase) ||
            candidate.HardwareName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            candidate.HardwareName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
        bool isTctlTdieIdentifier =
            candidate.SensorIdentifier.EndsWith(
                "/temperature/2",
                StringComparison.OrdinalIgnoreCase);

        if (isAmd &&
            isTctlTdieIdentifier &&
            candidate.SensorName.Equals(
                "Core (Tctl/Tdie)",
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (isAmd &&
            candidate.SensorName.Equals(
                "Core (Tctl/Tdie)",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (isAmd && isTctlTdieIdentifier)
        {
            return 2;
        }

        if (isAmd &&
            candidate.SensorName.Equals("Core (Tdie)", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (candidate.SensorName.Equals(
                "CPU Package",
                StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (isAmd &&
            candidate.SensorName.Equals("Core (Tctl)", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (candidate.SensorName.Contains("Package", StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        if (candidate.SensorName.Contains("Tdie", StringComparison.OrdinalIgnoreCase))
        {
            return 7;
        }

        return 100;
    }

    private void EnsureOpen()
    {
        if (_opened)
        {
            return;
        }

        try
        {
            _computer.Open();
            _opened = true;
        }
        catch
        {
            try
            {
                _computer.Close();
            }
            catch (Exception exception) when (SensorFailure.IsExpected(exception))
            {
                // Release any partially opened groups before the next probe.
            }

            throw;
        }
    }

    private static void CollectCandidates(
        IHardware hardware,
        string cpuName,
        string cpuIdentifier,
        ICollection<CpuTemperatureSensorCandidate> candidates)
    {
        hardware.Update();
        foreach (ISensor sensor in hardware.Sensors.Where(
                     sensor => sensor.SensorType == SensorType.Temperature))
        {
            candidates.Add(new CpuTemperatureSensorCandidate(
                cpuName,
                cpuIdentifier,
                sensor.Name,
                sensor.Identifier.ToString(),
                sensor.Value));
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            CollectCandidates(subHardware, cpuName, cpuIdentifier, candidates);
        }
    }

    private static CpuTemperatureProbeResult Unavailable(
        DateTimeOffset timestampUtc,
        string message) =>
        new()
        {
            TimestampUtc = timestampUtc,
            Message = message
        };
}
