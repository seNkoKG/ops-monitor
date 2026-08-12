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
            IsCpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsGpuEnabled = true,
            IsControllerEnabled = true,
            IsPowerMonitorEnabled = true
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
        => ReadSnapshot(timestampUtc).CpuTemperature;

    internal HardwareProbeResult ReadSnapshot(DateTimeOffset timestampUtc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            EnsureOpen();
            var candidates = new List<CpuTemperatureSensorCandidate>();
            var sensors = new List<HardwareSensorReading>();
            foreach (IHardware hardware in _computer.Hardware)
            {
                CollectSensors(
                    hardware,
                    hardware.Name,
                    hardware.Identifier.ToString(),
                    hardware.HardwareType.ToString(),
                    candidates,
                    sensors);
            }

            CpuTemperatureSensorCandidate? selected = SelectPreferredSensor(candidates);
            CpuTemperatureProbeResult cpu = selected is null
                ? Unavailable(
                    timestampUtc,
                    candidates.Count == 0
                        ? "No CPU temperature sensor was exposed. Elevated sensor access may be unavailable."
                        : "CPU temperature sensors returned no plausible live value.")
                : new CpuTemperatureProbeResult
                {
                    TemperatureCelsius = selected.TemperatureCelsius,
                    TimestampUtc = timestampUtc,
                    HardwareName = selected.HardwareName,
                    HardwareIdentifier = selected.HardwareIdentifier,
                    SensorName = selected.SensorName,
                    SensorIdentifier = selected.SensorIdentifier,
                    Message = $"{selected.SensorName} is live."
                };

            return new HardwareProbeResult
            {
                TimestampUtc = timestampUtc,
                Sensors = sensors,
                CpuTemperature = cpu,
                Message = sensors.Count == 0
                    ? "No supported hardware sensors were exposed."
                    : $"{sensors.Count} hardware sensors are live."
            };
        }
        catch (Exception exception) when (SensorFailure.IsExpected(exception))
        {
            string message = exception is UnauthorizedAccessException
                ? "Hardware sensor access was denied. Run the installed sensor task at highest privileges."
                : $"Hardware sensor probe failed: {exception.Message}";
            return new HardwareProbeResult
            {
                TimestampUtc = timestampUtc,
                Sensors = [],
                CpuTemperature = Unavailable(timestampUtc, message),
                Message = message
            };
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

    private static void CollectSensors(
        IHardware hardware,
        string rootName,
        string rootIdentifier,
        string rootType,
        ICollection<CpuTemperatureSensorCandidate> candidates,
        ICollection<HardwareSensorReading> readings)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception exception) when (SensorFailure.IsExpected(exception))
        {
            return;
        }

        foreach (ISensor sensor in hardware.Sensors)
        {
            string sensorType = sensor.SensorType.ToString();
            if (rootType.Equals(nameof(HardwareType.Cpu), StringComparison.Ordinal) &&
                sensor.SensorType == SensorType.Temperature)
            {
                candidates.Add(new CpuTemperatureSensorCandidate(
                    rootName,
                    rootIdentifier,
                    sensor.Name,
                    sensor.Identifier.ToString(),
                    sensor.Value));
            }

            if (Finite(sensor.Value) is not { } value || !IsUsefulReading(sensor))
            {
                continue;
            }

            readings.Add(new HardwareSensorReading(
                hardware.Name,
                hardware.Identifier.ToString(),
                hardware.HardwareType.ToString(),
                sensor.Name,
                sensor.Identifier.ToString(),
                sensorType,
                value));
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            CollectSensors(
                subHardware,
                rootName,
                rootIdentifier,
                rootType,
                candidates,
                readings);
        }
    }

    private static double? Finite(float? value) =>
        value is { } actual && float.IsFinite(actual) ? actual : null;

    private static bool IsUsefulReading(ISensor sensor)
    {
        string name = sensor.Name;
        if (sensor.SensorType == SensorType.Temperature &&
            (name.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("resolution", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("critical", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !name.Contains("threshold", StringComparison.OrdinalIgnoreCase);
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
