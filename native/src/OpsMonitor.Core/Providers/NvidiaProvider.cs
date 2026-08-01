using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using OpsMonitor.Core.Metrics;

namespace OpsMonitor.Core.Providers;

public sealed record NvidiaProviderOptions
{
    public TimeSpan Cadence { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan DiscoveryRetry { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public bool PreferNvml { get; init; } = true;
    public bool AllowNvidiaSmiFallback { get; init; } = true;
}

public sealed class NvidiaProvider : MetricProviderBase
{
    private static readonly MetricDescriptor[] MetricDescriptors =
    [
        new()
        {
            Id = WellKnownMetrics.GpuUtilization,
            DisplayName = "NVIDIA GPU utilization",
            ShortName = "GPU",
            Category = MetricCategory.Gpu,
            Unit = MetricUnit.Percent,
            ExpectedMinimum = 0,
            ExpectedMaximum = 100,
            PreferredDecimals = 0,
            HigherIsWorse = true
        },
        new()
        {
            Id = WellKnownMetrics.GpuTemperature,
            DisplayName = "NVIDIA GPU temperature",
            ShortName = "GPU temp",
            Category = MetricCategory.Gpu,
            Unit = MetricUnit.Celsius,
            ExpectedMinimum = 0,
            ExpectedMaximum = 125,
            PreferredDecimals = 0,
            HigherIsWorse = true
        },
        new()
        {
            Id = WellKnownMetrics.GpuMemoryUsedBytes,
            DisplayName = "NVIDIA GPU memory used",
            ShortName = "VRAM used",
            Category = MetricCategory.Gpu,
            Unit = MetricUnit.Bytes,
            PreferredDecimals = 0,
            HigherIsWorse = true
        },
        new()
        {
            Id = WellKnownMetrics.GpuMemoryTotalBytes,
            DisplayName = "NVIDIA GPU memory total",
            ShortName = "VRAM total",
            Category = MetricCategory.Gpu,
            Unit = MetricUnit.Bytes,
            PreferredDecimals = 0
        },
        new()
        {
            Id = WellKnownMetrics.GpuPowerWatts,
            DisplayName = "NVIDIA GPU board power",
            ShortName = "GPU power",
            Category = MetricCategory.Gpu,
            Unit = MetricUnit.Watts,
            PreferredDecimals = 1,
            HigherIsWorse = true
        },
        new()
        {
            Id = WellKnownMetrics.GpuFanPercent,
            DisplayName = "NVIDIA GPU fan",
            ShortName = "GPU fan",
            Category = MetricCategory.Cooling,
            Unit = MetricUnit.Percent,
            ExpectedMinimum = 0,
            ExpectedMaximum = 100,
            PreferredDecimals = 0
        },
        new()
        {
            Id = WellKnownMetrics.GpuClock,
            DisplayName = "NVIDIA GPU graphics clock",
            ShortName = "GPU clock",
            Category = MetricCategory.Gpu,
            Unit = MetricUnit.Hertz,
            PreferredDecimals = 0
        },
        new()
        {
            Id = WellKnownMetrics.GpuMemoryClock,
            DisplayName = "NVIDIA GPU memory clock",
            ShortName = "VRAM clock",
            Category = MetricCategory.Gpu,
            Unit = MetricUnit.Hertz,
            PreferredDecimals = 0
        }
    ];

    private readonly NvidiaProviderOptions _options;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private NvmlClient? _nvml;
    private string? _nvidiaSmiPath;
    private DateTimeOffset _nextDiscoveryUtc;
    private string _lastDiscoveryMessage = string.Empty;
    private bool _disposed;

    public NvidiaProvider(NvidiaProviderOptions? options = null)
    {
        _options = options ?? new NvidiaProviderOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _options.DiscoveryRetry,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _options.ProcessTimeout,
            TimeSpan.Zero);
    }

    public override string Id => "nvidia";
    public override string DisplayName => "NVIDIA GPU";
    public override IReadOnlyCollection<MetricDescriptor> Descriptors => MetricDescriptors;
    public override TimeSpan DefaultCadence => _options.Cadence;
    public override TimeSpan MinimumCadence => TimeSpan.FromMilliseconds(500);
    public override TimeSpan MaximumCadence => TimeSpan.FromMinutes(1);
    public override TimeSpan PollTimeout => _options.ProcessTimeout + TimeSpan.FromSeconds(2);

    public override async ValueTask<ProviderPollResult> PollAsync(
        MetricProviderContext context,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return MissingAll(
                    context.TimestampUtc,
                    MetricUnavailableReason.ProviderNotSupported,
                    "The NVIDIA provider currently requires Windows.");
            }

            DiscoverBackendIfDue(context.TimestampUtc);
            cancellationToken.ThrowIfCancellationRequested();

            if (_nvml is not null)
            {
                if (_nvml.TryCollect(cancellationToken, out var readings, out var message))
                {
                    return BuildResult(
                        context.TimestampUtc,
                        readings,
                        new MetricSource
                        {
                            Id = "nvidia.nvml",
                            DisplayName = "NVIDIA Management Library",
                            ProviderId = Id,
                            Kind = MetricSourceKind.VendorApi,
                            Detail = $"{readings.Count} physical GPU(s)"
                        });
                }

                _lastDiscoveryMessage = message;
                _nvml.Dispose();
                _nvml = null;
                _nextDiscoveryUtc = context.TimestampUtc + _options.DiscoveryRetry;
            }

            if (_options.AllowNvidiaSmiFallback && _nvidiaSmiPath is not null)
            {
                var fallback = await CollectWithNvidiaSmiAsync(
                        context.TimestampUtc,
                        _nvidiaSmiPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (fallback is not null)
                {
                    return fallback;
                }
            }

            return MissingAll(
                context.TimestampUtc,
                MetricUnavailableReason.HardwareNotPresent,
                string.IsNullOrWhiteSpace(_lastDiscoveryMessage)
                    ? "No supported NVIDIA telemetry backend was found."
                    : _lastDiscoveryMessage);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    protected override ValueTask DisposeAsyncCore()
    {
        if (!_disposed)
        {
            _disposed = true;
            _nvml?.Dispose();
            _nvml = null;
            _pollGate.Dispose();
        }

        return base.DisposeAsyncCore();
    }

    private void DiscoverBackendIfDue(DateTimeOffset nowUtc)
    {
        if (_nvml is not null || nowUtc < _nextDiscoveryUtc)
        {
            return;
        }

        _nextDiscoveryUtc = nowUtc + _options.DiscoveryRetry;
        var discoveryMessage = "NVML discovery is disabled.";
        if (_options.PreferNvml &&
            NvmlClient.TryCreate(out var nvml, out discoveryMessage))
        {
            _nvml = nvml;
            _lastDiscoveryMessage = string.Empty;
        }
        else
        {
            _lastDiscoveryMessage = discoveryMessage;
        }

        if (_options.AllowNvidiaSmiFallback)
        {
            _nvidiaSmiPath = FindNvidiaSmi();
        }
    }

    private async Task<ProviderPollResult?> CollectWithNvidiaSmiAsync(
        DateTimeOffset timestampUtc,
        string executable,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments =
                    "--query-gpu=utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw,fan.speed,clocks.gr,clocks.mem " +
                    "--format=csv,noheader,nounits",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        try
        {
            if (!process.Start())
            {
                _lastDiscoveryMessage = "nvidia-smi could not be started.";
                return null;
            }

            using var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ProcessTimeout);
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                _lastDiscoveryMessage =
                    $"nvidia-smi exceeded its {_options.ProcessTimeout.TotalSeconds:0.##} s timeout.";
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                _lastDiscoveryMessage =
                    $"nvidia-smi exited with code {process.ExitCode}: {error.Trim()}";
                return null;
            }

            var readings = ParseNvidiaSmi(output);
            if (readings.Count == 0)
            {
                _lastDiscoveryMessage = "nvidia-smi returned no usable GPU rows.";
                return null;
            }

            return BuildResult(
                timestampUtc,
                readings,
                new MetricSource
                {
                    Id = "nvidia.smi",
                    DisplayName = "NVIDIA System Management Interface",
                    ProviderId = Id,
                    Kind = MetricSourceKind.ExternalProcess,
                    Detail = $"{readings.Count} physical GPU(s); bounded fallback process"
                });
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            TryKill(process);
            _lastDiscoveryMessage = exception.Message;
            return null;
        }
    }

    private static ProviderPollResult BuildResult(
        DateTimeOffset timestampUtc,
        IReadOnlyList<NvidiaReading> readings,
        MetricSource source)
    {
        var tags = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["deviceCount"] = readings.Count.ToString(CultureInfo.InvariantCulture)
            });

        List<MetricSample> samples =
        [
            Aggregate(
                WellKnownMetrics.GpuUtilization,
                readings.Select(reading => reading.UtilizationPercent),
                values => values.Max(),
                timestampUtc,
                source,
                tags),
            Aggregate(
                WellKnownMetrics.GpuTemperature,
                readings.Select(reading => reading.TemperatureCelsius),
                values => values.Max(),
                timestampUtc,
                source,
                tags),
            Aggregate(
                WellKnownMetrics.GpuMemoryUsedBytes,
                readings.Select(reading => reading.MemoryUsedBytes),
                values => values.Sum(),
                timestampUtc,
                source,
                tags),
            Aggregate(
                WellKnownMetrics.GpuMemoryTotalBytes,
                readings.Select(reading => reading.MemoryTotalBytes),
                values => values.Sum(),
                timestampUtc,
                source,
                tags),
            Aggregate(
                WellKnownMetrics.GpuPowerWatts,
                readings.Select(reading => reading.PowerWatts),
                values => values.Sum(),
                timestampUtc,
                source,
                tags),
            Aggregate(
                WellKnownMetrics.GpuFanPercent,
                readings.Select(reading => reading.FanPercent),
                values => values.Max(),
                timestampUtc,
                source,
                tags),
            Aggregate(
                WellKnownMetrics.GpuClock,
                readings.Select(reading => MegahertzToHertz(reading.GraphicsClockMegahertz)),
                values => values.Max(),
                timestampUtc,
                source,
                tags),
            Aggregate(
                WellKnownMetrics.GpuMemoryClock,
                readings.Select(reading => MegahertzToHertz(reading.MemoryClockMegahertz)),
                values => values.Max(),
                timestampUtc,
                source,
                tags)
        ];

        var coreAvailable = samples
            .Where(sample => sample.MetricId is var id &&
                             (id == WellKnownMetrics.GpuUtilization ||
                              id == WellKnownMetrics.GpuTemperature))
            .Any(sample => sample.HasUsableValue);

        return new ProviderPollResult
        {
            Samples = samples,
            HealthState = coreAvailable
                ? ProviderHealthState.Healthy
                : ProviderHealthState.Degraded,
            Reason = coreAvailable
                ? MetricUnavailableReason.None
                : MetricUnavailableReason.InvalidData,
            Message = coreAvailable
                ? string.Empty
                : "The NVIDIA backend returned no core telemetry."
        };
    }

    private static MetricSample Aggregate(
        MetricId metricId,
        IEnumerable<double?> values,
        Func<IEnumerable<double>, double> aggregate,
        DateTimeOffset timestampUtc,
        MetricSource source,
        IReadOnlyDictionary<string, string> tags)
    {
        var available = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return available.Length == 0
            ? MetricSample.Missing(
                metricId,
                timestampUtc,
                source,
                MetricAvailability.Unavailable,
                MetricUnavailableReason.HardwareNotPresent,
                "This sensor is not exposed by the installed GPU or driver.")
            : MetricSample.Available(
                metricId,
                aggregate(available),
                timestampUtc,
                source,
                tags);
    }

    private static List<NvidiaReading> ParseNvidiaSmi(string output)
    {
        const double mebibyte = 1024d * 1024d;
        List<NvidiaReading> readings = [];
        foreach (var line in output.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length < 8)
            {
                continue;
            }

            readings.Add(new NvidiaReading(
                ParseNullable(fields[0]),
                ParseNullable(fields[1]),
                Multiply(ParseNullable(fields[2]), mebibyte),
                Multiply(ParseNullable(fields[3]), mebibyte),
                ParseNullable(fields[4]),
                ParseNullable(fields[5]),
                ParseNullable(fields[6]),
                ParseNullable(fields[7])));
        }

        return readings;
    }

    private static double? ParseNullable(string value)
    {
        value = value.Trim();
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static double? Multiply(double? value, double multiplier) =>
        value.HasValue ? value.Value * multiplier : null;

    private static double? MegahertzToHertz(double? value) =>
        value.HasValue ? value.Value * 1_000_000d : null;

    private static ProviderPollResult MissingAll(
        DateTimeOffset timestampUtc,
        MetricUnavailableReason reason,
        string message)
    {
        var source = new MetricSource
        {
            Id = "nvidia.discovery",
            DisplayName = "NVIDIA provider discovery",
            ProviderId = "nvidia",
            Kind = MetricSourceKind.VendorApi
        };

        return new ProviderPollResult
        {
            Samples = MetricDescriptors
                .Select(descriptor => MetricSample.Missing(
                    descriptor.Id,
                    timestampUtc,
                    source,
                    MetricAvailability.Unavailable,
                    reason,
                    message))
                .ToArray(),
            HealthState = ProviderHealthState.Unavailable,
            Reason = reason,
            Message = message
        };
    }

    private static string? FindNvidiaSmi()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation",
                "NVSMI",
                "nvidia-smi.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may already have exited or access may have disappeared.
        }
    }

    private sealed record NvidiaReading(
        double? UtilizationPercent,
        double? TemperatureCelsius,
        double? MemoryUsedBytes,
        double? MemoryTotalBytes,
        double? PowerWatts,
        double? FanPercent,
        double? GraphicsClockMegahertz,
        double? MemoryClockMegahertz);

    private sealed class NvmlClient : IDisposable
    {
        private const int Success = 0;
        private readonly nint _library;
        private readonly NvmlShutdown _shutdown;
        private readonly NvmlGetUtilization _getUtilization;
        private readonly NvmlGetTemperature _getTemperature;
        private readonly NvmlGetMemoryInfo _getMemoryInfo;
        private readonly NvmlGetUnsignedValue? _getPowerUsage;
        private readonly NvmlGetUnsignedValue? _getFanSpeed;
        private readonly NvmlGetClockInfo? _getClockInfo;
        private readonly nint[] _devices;
        private bool _disposed;

        private NvmlClient(
            nint library,
            NvmlShutdown shutdown,
            NvmlGetUtilization getUtilization,
            NvmlGetTemperature getTemperature,
            NvmlGetMemoryInfo getMemoryInfo,
            NvmlGetUnsignedValue? getPowerUsage,
            NvmlGetUnsignedValue? getFanSpeed,
            NvmlGetClockInfo? getClockInfo,
            nint[] devices)
        {
            _library = library;
            _shutdown = shutdown;
            _getUtilization = getUtilization;
            _getTemperature = getTemperature;
            _getMemoryInfo = getMemoryInfo;
            _getPowerUsage = getPowerUsage;
            _getFanSpeed = getFanSpeed;
            _getClockInfo = getClockInfo;
            _devices = devices;
        }

        public static bool TryCreate(out NvmlClient? client, out string message)
        {
            client = null;
            message = string.Empty;
            if (!TryLoadLibrary(out var library))
            {
                message = "NVML was not found.";
                return false;
            }

            NvmlShutdown? shutdown = null;
            try
            {
                var initialize = GetRequiredDelegate<NvmlInitialize>(library, "nvmlInit_v2");
                shutdown = GetRequiredDelegate<NvmlShutdown>(library, "nvmlShutdown");
                var getCount = GetRequiredDelegate<NvmlGetDeviceCount>(
                    library,
                    "nvmlDeviceGetCount_v2",
                    "nvmlDeviceGetCount");
                var getHandle = GetRequiredDelegate<NvmlGetDeviceHandle>(
                    library,
                    "nvmlDeviceGetHandleByIndex_v2",
                    "nvmlDeviceGetHandleByIndex");
                var getUtilization = GetRequiredDelegate<NvmlGetUtilization>(
                    library,
                    "nvmlDeviceGetUtilizationRates");
                var getTemperature = GetRequiredDelegate<NvmlGetTemperature>(
                    library,
                    "nvmlDeviceGetTemperature");
                var getMemory = GetRequiredDelegate<NvmlGetMemoryInfo>(
                    library,
                    "nvmlDeviceGetMemoryInfo");

                var result = initialize();
                if (result != Success)
                {
                    message = $"NVML initialization failed with status {result}.";
                    NativeLibrary.Free(library);
                    return false;
                }

                uint count = 0;
                result = getCount(ref count);
                if (result != Success || count == 0)
                {
                    shutdown();
                    NativeLibrary.Free(library);
                    message = result == Success
                        ? "NVML reported no NVIDIA GPUs."
                        : $"NVML device discovery failed with status {result}.";
                    return false;
                }

                var devices = new List<nint>((int)count);
                for (uint index = 0; index < count; index++)
                {
                    nint handle = 0;
                    if (getHandle(index, ref handle) == Success && handle != 0)
                    {
                        devices.Add(handle);
                    }
                }

                if (devices.Count == 0)
                {
                    shutdown();
                    NativeLibrary.Free(library);
                    message = "NVML exposed no usable device handles.";
                    return false;
                }

                client = new NvmlClient(
                    library,
                    shutdown,
                    getUtilization,
                    getTemperature,
                    getMemory,
                    TryGetDelegate<NvmlGetUnsignedValue>(
                        library,
                        "nvmlDeviceGetPowerUsage"),
                    TryGetDelegate<NvmlGetUnsignedValue>(
                        library,
                        "nvmlDeviceGetFanSpeed"),
                    TryGetDelegate<NvmlGetClockInfo>(
                        library,
                        "nvmlDeviceGetClockInfo"),
                    devices.ToArray());
                return true;
            }
            catch (Exception exception) when (
                exception is EntryPointNotFoundException or
                    InvalidOperationException or
                    MarshalDirectiveException)
            {
                try
                {
                    shutdown?.Invoke();
                }
                catch
                {
                    // Best-effort rollback after incomplete NVML initialization.
                }

                NativeLibrary.Free(library);
                message = exception.Message;
                return false;
            }
        }

        public bool TryCollect(
            CancellationToken cancellationToken,
            out IReadOnlyList<NvidiaReading> readings,
            out string message)
        {
            List<NvidiaReading> collected = [];
            foreach (var device in _devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var utilization = new NvmlUtilization();
                uint temperature = 0;
                var memory = new NvmlMemory();
                uint power = 0;
                uint fan = 0;
                uint graphicsClock = 0;
                uint memoryClock = 0;

                var utilizationValue =
                    _getUtilization(device, ref utilization) == Success
                        ? utilization.Gpu
                        : (uint?)null;
                var temperatureValue =
                    _getTemperature(device, 0, ref temperature) == Success
                        ? temperature
                        : (uint?)null;
                var memoryAvailable = _getMemoryInfo(device, ref memory) == Success;
                var powerValue = _getPowerUsage is not null &&
                                 _getPowerUsage(device, ref power) == Success
                    ? power / 1000d
                    : (double?)null;
                var fanValue = _getFanSpeed is not null &&
                               _getFanSpeed(device, ref fan) == Success
                    ? fan
                    : (uint?)null;
                var graphicsClockValue = _getClockInfo is not null &&
                                         _getClockInfo(device, 0, ref graphicsClock) == Success
                    ? graphicsClock
                    : (uint?)null;
                var memoryClockValue = _getClockInfo is not null &&
                                       _getClockInfo(device, 2, ref memoryClock) == Success
                    ? memoryClock
                    : (uint?)null;

                collected.Add(new NvidiaReading(
                    utilizationValue,
                    temperatureValue,
                    memoryAvailable ? memory.Used : null,
                    memoryAvailable ? memory.Total : null,
                    powerValue,
                    fanValue,
                    graphicsClockValue,
                    memoryClockValue));
            }

            readings = collected;
            if (collected.Any(reading =>
                    reading.UtilizationPercent.HasValue ||
                    reading.TemperatureCelsius.HasValue))
            {
                message = string.Empty;
                return true;
            }

            message = "NVML returned no usable utilization or temperature values.";
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _shutdown();
            }
            finally
            {
                NativeLibrary.Free(_library);
            }
        }

        private static bool TryLoadLibrary(out nint library)
        {
            var candidates = new[]
            {
                "nvml.dll",
                Path.Combine(Environment.SystemDirectory, "nvml.dll"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "NVIDIA Corporation",
                    "NVSMI",
                    "nvml.dll")
            };

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (NativeLibrary.TryLoad(candidate, out library))
                {
                    return true;
                }
            }

            library = 0;
            return false;
        }

        private static T GetRequiredDelegate<T>(
            nint library,
            params string[] names)
            where T : Delegate
        {
            foreach (var name in names)
            {
                if (NativeLibrary.TryGetExport(library, name, out var address))
                {
                    return Marshal.GetDelegateForFunctionPointer<T>(address);
                }
            }

            throw new EntryPointNotFoundException(
                $"NVML export was not found: {string.Join(" or ", names)}.");
        }

        private static T? TryGetDelegate<T>(nint library, string name)
            where T : Delegate =>
            NativeLibrary.TryGetExport(library, name, out var address)
                ? Marshal.GetDelegateForFunctionPointer<T>(address)
                : null;

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlUtilization
        {
            internal uint Gpu;
            internal uint Memory;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlMemory
        {
            internal ulong Total;
            internal ulong Free;
            internal ulong Used;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlInitialize();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlShutdown();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlGetDeviceCount(ref uint count);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlGetDeviceHandle(uint index, ref nint device);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlGetUtilization(nint device, ref NvmlUtilization utilization);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlGetTemperature(
            nint device,
            uint sensorType,
            ref uint temperature);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlGetMemoryInfo(nint device, ref NvmlMemory memory);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlGetUnsignedValue(nint device, ref uint value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlGetClockInfo(
            nint device,
            uint clockType,
            ref uint clockMegahertz);
    }
}
