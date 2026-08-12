using System.Globalization;
using OpsMonitor.Core.Metrics;
using OpsMonitor.Core.Providers;
using OpsMonitor.SensorBridge;

var tests = new (string Name, Func<Task> Run)[]
{
    ("AMD Tctl/Tdie wins deterministic sensor selection", TestAmdSelectionAsync),
    ("zero and implausible temperatures are rejected", TestPlausibilityAsync),
    ("bridge payload is consumed by Core", TestPayloadAsync),
    ("versioned hardware catalog lights curated and dynamic metrics", TestHardwareCatalogAsync),
    ("atomic writer replaces complete payloads", TestAtomicWriteAsync),
    ("atomic writer recovers from a transient publication lock", TestAtomicWriteRetryAsync),
    ("unavailable probes are periodically reopened with backoff", TestProbeResetAsync),
    ("probe recovery backoff grows and caps", TestProbeBackoffAsync),
    ("publication failures leave a healthy probe alone", TestPublicationIsolationAsync),
    ("widget return cancels a pending bridge shutdown", TestShutdownRaceAsync),
    ("command line options are clamped and explicit", TestOptionsAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}");
        Console.WriteLine(exception);
    }
}

return failures.Count == 0 ? 0 : 1;

static Task TestAmdSelectionAsync()
{
    var candidates = new[]
    {
        Candidate("CCD1 (Tdie)", "/amdcpu/0/temperature/3", 58.5),
        Candidate("Core (Tctl)", "/amdcpu/0/temperature/0", 70),
        Candidate("Core (Tctl/Tdie)", "/amdcpu/0/temperature/2", 61.875),
        Candidate("CPU Package", "/intelcpu/0/temperature/0", 39)
    };

    CpuTemperatureSensorCandidate? selected =
        LibreHardwareMonitorCpuProbe.SelectPreferredSensor(candidates);
    CpuTemperatureSensorCandidate actual =
        selected ?? throw new InvalidOperationException("no sensor selected");
    Assert(
        actual.SensorName == "Core (Tctl/Tdie)",
        $"wrong sensor selected: {actual.SensorName}");
    Assert(
        actual.SensorIdentifier == "/amdcpu/0/temperature/2",
        $"wrong identifier selected: {actual.SensorIdentifier}");
    return Task.CompletedTask;
}

static Task TestPlausibilityAsync()
{
    var candidates = new[]
    {
        Candidate("Core (Tctl/Tdie)", "/amdcpu/0/temperature/2", 0),
        Candidate("CPU Package", "/intelcpu/0/temperature/0", 126),
        Candidate("Core (Tdie)", "/amdcpu/0/temperature/1", null)
    };

    Assert(
        LibreHardwareMonitorCpuProbe.SelectPreferredSensor(candidates) is null,
        "an unavailable or implausible sensor was selected");
    return Task.CompletedTask;
}

static async Task TestPayloadAsync()
{
    var timestamp = DateTimeOffset.Parse(
        "2026-07-26T04:15:00Z",
        CultureInfo.InvariantCulture);
    var result = new CpuTemperatureProbeResult
    {
        TimestampUtc = timestamp,
        TemperatureCelsius = 61.875,
        HardwareIdentifier = "/amdcpu/0",
        SensorName = "Core (Tctl/Tdie)",
        SensorIdentifier = "/amdcpu/0/temperature/2",
        Message = "Core (Tctl/Tdie) is live."
    };

    string payload = SensorBridgeHost.FormatPayload(result);
    string[] fields = payload.Split('|');
    Assert(fields.Length == 2, "payload field count changed");
    Assert(
        double.TryParse(
            fields[0],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double value) &&
        value == 61.875,
        "payload temperature is not invariant");
    Assert(
        long.TryParse(
            fields[1],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long ticks) &&
        ticks == timestamp.UtcDateTime.Ticks,
        "payload timestamp is not UTC ticks");

    string firstState = SensorBridgeDiagnostics.GetStateKey(result);
    string secondState = SensorBridgeDiagnostics.GetStateKey(result with
    {
        TimestampUtc = timestamp.AddSeconds(3),
        TemperatureCelsius = 62.125
    });
    Assert(
        firstState == secondState,
        "live value changes would cause unnecessary diagnostic disk writes");

    var directory = Directory.CreateTempSubdirectory("OpsMonitorSensorContract-");
    try
    {
        string path = Path.Combine(directory.FullName, "cpu-temperature.txt");
        await AtomicTextFile.WriteAsync(path, payload, CancellationToken.None);
        await using var provider = new CpuTemperatureBridgeProvider(
            new CpuTemperatureBridgeOptions
            {
                ReadingPath = path
            });
        ProviderPollResult poll = await provider.PollAsync(
            new MetricProviderContext
            {
                TimestampUtc = timestamp.AddSeconds(1),
                LatestSamples = new Dictionary<MetricId, MetricSample>()
            },
            CancellationToken.None);
        MetricSample sample = poll.Samples.Single();
        Assert(sample.HasUsableValue, "Core rejected a fresh bridge payload");
        Assert(
            sample.Value == 61.875,
            $"Core changed the published value to {sample.Value}");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static async Task TestHardwareCatalogAsync()
{
    var timestamp = DateTimeOffset.Parse(
        "2026-07-30T10:00:00Z",
        CultureInfo.InvariantCulture);
    var snapshot = new HardwareProbeResult
    {
        TimestampUtc = timestamp,
        CpuTemperature = new CpuTemperatureProbeResult
        {
            TimestampUtc = timestamp,
            TemperatureCelsius = 63,
            SensorName = "Core (Tctl/Tdie)",
            SensorIdentifier = "/amdcpu/0/temperature/2"
        },
        Sensors =
        [
            new("Ryzen", "/amdcpu/0", "Cpu", "Cores (Average Effective)", "/amdcpu/0/clock/0", "Clock", 5_000),
            new("Ryzen", "/amdcpu/0", "Cpu", "Package", "/amdcpu/0/power/0", "Power", 75),
            new("Radeon", "/gpu-amd/0", "GpuAmd", "GPU Core", "/gpu-amd/0/load/0", "Load", 67),
            new("Radeon", "/gpu-amd/0", "GpuAmd", "GPU Core", "/gpu-amd/0/temperature/0", "Temperature", 54),
            new("Radeon", "/gpu-amd/0", "GpuAmd", "GPU Core", "/gpu-amd/0/clock/0", "Clock", 2_450),
            new("Radeon", "/gpu-amd/0", "GpuAmd", "GPU Memory Used", "/gpu-amd/0/smalldata/0", "SmallData", 6_144),
            new("Radeon", "/gpu-amd/0", "GpuAmd", "GPU Memory Total", "/gpu-amd/0/smalldata/1", "SmallData", 12_288),
            new("NVMe", "/nvme/0", "Storage", "Composite Temperature", "/nvme/0/temperature/0", "Temperature", 42),
            new("NVMe", "/nvme/0", "Storage", "Remaining Life", "/nvme/0/level/0", "Level", 97),
            new("NVMe", "/nvme/0", "Storage", "Read Rate", "/nvme/0/throughput/0", "Throughput", 8_000_000)
        ]
    };

    var directory = Directory.CreateTempSubdirectory("OpsMonitorHardwareCatalog-");
    try
    {
        string path = Path.Combine(directory.FullName, "hardware-sensors.json");
        await AtomicTextFile.WriteAsync(
            path,
            SensorBridgeHost.FormatCatalogPayload(snapshot),
            CancellationToken.None);
        await using var provider = new HardwareSensorBridgeProvider(
            new HardwareSensorBridgeOptions { SnapshotPath = path });
        ProviderPollResult poll = await provider.PollAsync(
            new MetricProviderContext
            {
                TimestampUtc = timestamp.AddSeconds(1),
                LatestSamples = new Dictionary<MetricId, MetricSample>()
            },
            CancellationToken.None);

        Assert(
            poll.HealthState == ProviderHealthState.Healthy,
            $"catalog health was {poll.HealthState}");
        Assert(
            poll.Descriptors.Any(item =>
                item.Id == HardwareSensorBridgeProvider.GetMetricId("/amdcpu/0/clock/0")),
            "dynamic CPU clock descriptor was not published");
        Assert(
            poll.Samples.Any(item =>
                item.MetricId == WellKnownMetrics.CpuClock &&
                item.Value == 5_000_000_000d),
            "curated CPU clock was not converted from MHz to Hz");
        Assert(
            poll.Samples.Any(item =>
                item.MetricId == WellKnownMetrics.CpuPackagePower &&
                item.Value == 75),
            "curated CPU package power was not published");
        Assert(
            poll.Samples.Any(item =>
                item.MetricId == WellKnownMetrics.GpuPrimaryUtilization &&
                item.Value == 67),
            "vendor-neutral GPU utilization was not published");
        Assert(
            poll.Samples.Any(item =>
                item.MetricId == WellKnownMetrics.GpuPrimaryTemperature &&
                item.Value == 54),
            "vendor-neutral GPU temperature was not published");
        Assert(
            poll.Samples.Any(item =>
                item.MetricId == WellKnownMetrics.GpuPrimaryClock &&
                item.Value == 2_450_000_000d),
            "vendor-neutral GPU clock was not converted from MHz to Hz");
        Assert(
            poll.Samples.Any(item =>
                item.MetricId == WellKnownMetrics.GpuPrimaryMemoryUsedBytes &&
                item.Value == 6_144d * 1024d * 1024d),
            "vendor-neutral VRAM use was not converted from MiB to bytes");
        Assert(
            poll.Samples.Any(item =>
                item.MetricId == WellKnownMetrics.StorageTemperature &&
                item.Value == 42),
            "curated storage temperature was not published");
        Assert(
            poll.Samples.Any(item =>
                item.MetricId == WellKnownMetrics.StorageHealthPercent &&
                item.Value == 97),
            "curated storage health was not published");
        Assert(
            poll.Samples.Any(item =>
                item.MetricId == WellKnownMetrics.StorageReadRate &&
                item.Value == 8_000_000),
            "storage throughput was incorrectly rescaled");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static async Task TestAtomicWriteAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorSensorBridge-");
    try
    {
        string path = Path.Combine(directory.FullName, "cpu-temperature.txt");
        await AtomicTextFile.WriteAsync(path, "40|1", CancellationToken.None);
        await AtomicTextFile.WriteAsync(path, "61.875|2", CancellationToken.None);

        Assert(
            await File.ReadAllTextAsync(path) == "61.875|2",
            "atomic replacement did not publish the complete latest payload");
        Assert(
            directory.GetFiles("*.tmp").Length == 0,
            "atomic writer left a temporary file behind");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static async Task TestAtomicWriteRetryAsync()
{
    var directory = Directory.CreateTempSubdirectory("OpsMonitorSensorRetry-");
    try
    {
        string path = Path.Combine(directory.FullName, "cpu-temperature.txt");
        await File.WriteAllTextAsync(path, "40|1");
        Task writeTask;
        using (var blocker = new FileStream(
                   path,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        {
            writeTask = AtomicTextFile.WriteAsync(
                path,
                "62.5|2",
                CancellationToken.None);
            await Task.Delay(150);
            Assert(!writeTask.IsCompleted, "the publication lock did not block replacement");
        }

        await writeTask;
        Assert(
            await File.ReadAllTextAsync(path) == "62.5|2",
            "the atomic writer did not recover after the lock was released");
        Assert(
            directory.GetFiles("*.tmp").Length == 0,
            "retry recovery left a temporary file behind");
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static Task TestProbeResetAsync()
{
    var recovery = new ProbeRecoveryPolicy();
    var timestamp = DateTimeOffset.Parse(
        "2026-07-27T12:00:00Z",
        CultureInfo.InvariantCulture);

    Assert(
        !recovery.RecordUnavailable(timestamp),
        "the hardware probe was reset after one unavailable poll");
    Assert(
        !recovery.RecordUnavailable(timestamp.AddSeconds(1)),
        "the hardware probe was reset after two unavailable polls");
    Assert(
        recovery.RecordUnavailable(timestamp.AddSeconds(2)),
        "a repeatedly unavailable hardware probe was never reopened");
    Assert(
        !recovery.CanAttempt(timestamp.AddSeconds(3)),
        "the failed probe was immediately reopened without backoff");
    Assert(
        recovery.CanAttempt(timestamp.AddSeconds(4)),
        "the probe remained blocked after its recovery delay");
    Assert(
        SensorBridgeHost.ShouldResetProbeAfterUnavailablePolls(3),
        "the compatibility reset threshold changed");
    return Task.CompletedTask;
}

static Task TestProbeBackoffAsync()
{
    var recovery = new ProbeRecoveryPolicy();
    var timestamp = DateTimeOffset.Parse(
        "2026-07-27T12:00:00Z",
        CultureInfo.InvariantCulture);
    TimeSpan[] expected =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(30)
    ];

    for (int attempt = 0; attempt < expected.Length; attempt++)
    {
        DateTimeOffset failureTime = timestamp.AddMinutes(attempt);
        Assert(
            recovery.RecordFailure(
                SensorBridgeFailureSource.Probe,
                failureTime),
            "a probe fault did not request probe disposal");
        Assert(
            recovery.NextAttemptUtc - failureTime == expected[attempt],
            $"probe backoff {attempt} was " +
            $"{recovery.NextAttemptUtc - failureTime}, expected {expected[attempt]}");
    }

    Assert(
        ProbeRecoveryPolicy.GetDelay(100) == ProbeRecoveryPolicy.MaximumDelay,
        "probe recovery delay was not capped");

    recovery.RecordAvailable();
    Assert(recovery.RecoveryAttempts == 0, "a healthy read did not reset backoff");
    Assert(
        recovery.CanAttempt(timestamp),
        "a healthy read left the probe in a recovery delay");
    return Task.CompletedTask;
}

static Task TestPublicationIsolationAsync()
{
    var recovery = new ProbeRecoveryPolicy();
    var timestamp = DateTimeOffset.Parse(
        "2026-07-27T12:00:00Z",
        CultureInfo.InvariantCulture);

    recovery.RecordAvailable();
    DateTimeOffset nextAttemptBefore = recovery.NextAttemptUtc;
    int attemptsBefore = recovery.RecoveryAttempts;
    bool shouldDispose = recovery.RecordFailure(
        SensorBridgeFailureSource.Publication,
        timestamp);

    Assert(!shouldDispose, "an output publication fault disposed a healthy probe");
    Assert(
        recovery.NextAttemptUtc == nextAttemptBefore,
        "an output publication fault scheduled hardware recovery");
    Assert(
        recovery.RecoveryAttempts == attemptsBefore,
        "an output publication fault increased hardware recovery backoff");
    return Task.CompletedTask;
}

static Task TestShutdownRaceAsync()
{
    Assert(
        !SensorBridgeHost.ShouldExitAfterDelay(4, false, true),
        "the bridge would exit after the widget returned during its delay");
    Assert(
        SensorBridgeHost.ShouldExitAfterDelay(4, false, false),
        "the bridge did not exit after four confirmed missing-widget polls");
    Assert(
        !SensorBridgeHost.ShouldExitAfterDelay(4, true, false),
        "--stay-alive did not suppress automatic shutdown");
    return Task.CompletedTask;
}

static Task TestOptionsAsync()
{
    bool parsed = SensorBridgeOptions.TryParse(
        ["--once", "--interval-ms", "1000", "--output", ".\\reading.txt"],
        out SensorBridgeOptions options,
        out string error);
    Assert(parsed, $"valid options failed: {error}");
    Assert(options.Once, "--once was not retained");
    Assert(
        options.Interval == TimeSpan.FromSeconds(1),
        "minimum cadence was not retained");
    Assert(Path.IsPathFullyQualified(options.OutputPath), "output path was not normalized");

    Assert(
        !SensorBridgeOptions.TryParse(
            ["--interval-ms", "999"],
            out _,
            out _),
        "sub-second cadence was accepted");
    Assert(
        !SensorBridgeOptions.TryParse(
            ["--once", "--stay-alive"],
            out _,
            out _),
        "conflicting lifetime options were accepted");
    return Task.CompletedTask;
}

static CpuTemperatureSensorCandidate Candidate(
    string sensorName,
    string sensorIdentifier,
    double? value) =>
    new(
        "AMD Ryzen 7 9800X3D",
        "/amdcpu/0",
        sensorName,
        sensorIdentifier,
        value);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
