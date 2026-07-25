using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using OpsMonitor.Core.Metrics;

namespace OpsMonitor.Core.Providers;

public sealed class WindowsNativeProvider : MetricProviderBase
{
    private static readonly MetricSource Source = new()
    {
        Id = "windows.native",
        DisplayName = "Windows",
        ProviderId = "windows.native",
        Kind = MetricSourceKind.WindowsNative
    };

    private static readonly MetricDescriptor[] MetricDescriptors =
    [
        new()
        {
            Id = WellKnownMetrics.CpuTotalUtilization,
            DisplayName = "CPU utilization",
            ShortName = "CPU",
            Category = MetricCategory.Cpu,
            Unit = MetricUnit.Percent,
            ExpectedMinimum = 0,
            ExpectedMaximum = 100,
            PreferredDecimals = 0,
            HigherIsWorse = true,
            Description = "Total processor time in use across all logical processors."
        },
        new()
        {
            Id = WellKnownMetrics.MemoryUsedBytes,
            DisplayName = "Physical memory used",
            ShortName = "RAM used",
            Category = MetricCategory.Memory,
            Unit = MetricUnit.Bytes,
            PreferredDecimals = 0,
            HigherIsWorse = true
        },
        new()
        {
            Id = WellKnownMetrics.MemoryAvailableBytes,
            DisplayName = "Physical memory available",
            ShortName = "RAM available",
            Category = MetricCategory.Memory,
            Unit = MetricUnit.Bytes,
            PreferredDecimals = 0
        },
        new()
        {
            Id = WellKnownMetrics.MemoryTotalBytes,
            DisplayName = "Physical memory total",
            ShortName = "RAM total",
            Category = MetricCategory.Memory,
            Unit = MetricUnit.Bytes,
            PreferredDecimals = 0
        },
        new()
        {
            Id = WellKnownMetrics.MemoryUtilization,
            DisplayName = "Physical memory utilization",
            ShortName = "RAM",
            Category = MetricCategory.Memory,
            Unit = MetricUnit.Percent,
            ExpectedMinimum = 0,
            ExpectedMaximum = 100,
            PreferredDecimals = 0,
            HigherIsWorse = true
        },
        new()
        {
            Id = WellKnownMetrics.NetworkDownloadRate,
            DisplayName = "Network download",
            ShortName = "Down",
            Category = MetricCategory.Network,
            Unit = MetricUnit.BytesPerSecond,
            Aggregation = MetricAggregationKind.Rate,
            PreferredDecimals = 0
        },
        new()
        {
            Id = WellKnownMetrics.NetworkUploadRate,
            DisplayName = "Network upload",
            ShortName = "Up",
            Category = MetricCategory.Network,
            Unit = MetricUnit.BytesPerSecond,
            Aggregation = MetricAggregationKind.Rate,
            PreferredDecimals = 0
        },
        new()
        {
            Id = WellKnownMetrics.SystemUptime,
            DisplayName = "System uptime",
            ShortName = "Uptime",
            Category = MetricCategory.System,
            Unit = MetricUnit.Seconds,
            Aggregation = MetricAggregationKind.Duration,
            PreferredDecimals = 0
        },
        new()
        {
            Id = WellKnownMetrics.BatteryCharge,
            DisplayName = "Battery charge",
            ShortName = "Battery",
            Category = MetricCategory.Battery,
            Unit = MetricUnit.Percent,
            ExpectedMinimum = 0,
            ExpectedMaximum = 100,
            PreferredDecimals = 0
        },
        new()
        {
            Id = WellKnownMetrics.BatteryAcOnline,
            DisplayName = "AC power connected",
            ShortName = "AC",
            Category = MetricCategory.Battery,
            Unit = MetricUnit.None,
            Aggregation = MetricAggregationKind.State,
            ExpectedMinimum = 0,
            ExpectedMaximum = 1
        },
        new()
        {
            Id = WellKnownMetrics.BatteryRemaining,
            DisplayName = "Estimated battery time remaining",
            ShortName = "Remaining",
            Category = MetricCategory.Battery,
            Unit = MetricUnit.Seconds,
            Aggregation = MetricAggregationKind.Duration,
            PreferredDecimals = 0
        },
        new()
        {
            Id = WellKnownMetrics.BatterySaver,
            DisplayName = "Battery saver active",
            ShortName = "Saver",
            Category = MetricCategory.Battery,
            Unit = MetricUnit.None,
            Aggregation = MetricAggregationKind.State,
            ExpectedMinimum = 0,
            ExpectedMaximum = 1
        }
    ];

    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly Dictionary<string, NetworkCounters> _previousNetwork =
        new(StringComparer.Ordinal);
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private long _previousNetworkTimestamp;
    private bool _disposed;

    public override string Id => "windows.native";
    public override string DisplayName => "Windows native metrics";
    public override IReadOnlyCollection<MetricDescriptor> Descriptors => MetricDescriptors;
    public override TimeSpan DefaultCadence => TimeSpan.FromSeconds(1);
    public override TimeSpan MinimumCadence => TimeSpan.FromMilliseconds(500);
    public override TimeSpan MaximumCadence => TimeSpan.FromSeconds(30);
    public override TimeSpan PollTimeout => TimeSpan.FromSeconds(3);

    public override async ValueTask<ProviderPollResult> PollAsync(
        MetricProviderContext context,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows())
            {
                return Unsupported(context.TimestampUtc);
            }

            List<MetricSample> samples = [];
            var degradedMessages = new List<string>();

            ReadCpu(context.TimestampUtc, samples, degradedMessages);
            ReadMemory(context.TimestampUtc, samples, degradedMessages);
            ReadNetwork(context.TimestampUtc, samples, degradedMessages);
            ReadUptime(context.TimestampUtc, samples);
            ReadBattery(context.TimestampUtc, samples, degradedMessages);

            return new ProviderPollResult
            {
                Samples = samples,
                HealthState = degradedMessages.Count == 0
                    ? ProviderHealthState.Healthy
                    : ProviderHealthState.Degraded,
                Reason = degradedMessages.Count == 0
                    ? MetricUnavailableReason.None
                    : MetricUnavailableReason.HardwareNotPresent,
                Message = string.Join(" ", degradedMessages.Distinct(StringComparer.Ordinal))
            };
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
            _pollGate.Dispose();
        }

        return base.DisposeAsyncCore();
    }

    private void ReadCpu(
        DateTimeOffset timestampUtc,
        List<MetricSample> samples,
        List<string> diagnostics)
    {
        if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            var message = GetLastNativeError("GetSystemTimes");
            diagnostics.Add(message);
            samples.Add(MetricSample.Missing(
                WellKnownMetrics.CpuTotalUtilization,
                timestampUtc,
                Source,
                MetricAvailability.Error,
                MetricUnavailableReason.ProviderFaulted,
                message));
            return;
        }

        var idle = idleTime.Value;
        var kernel = kernelTime.Value;
        var user = userTime.Value;
        if (_previousKernel == 0)
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            samples.Add(MetricSample.Missing(
                WellKnownMetrics.CpuTotalUtilization,
                timestampUtc,
                Source,
                MetricAvailability.Initializing,
                MetricUnavailableReason.FirstSamplePending,
                "A second system-time sample is required."));
            return;
        }

        var idleDelta = idle >= _previousIdle ? idle - _previousIdle : 0;
        var kernelDelta = kernel >= _previousKernel ? kernel - _previousKernel : 0;
        var userDelta = user >= _previousUser ? user - _previousUser : 0;
        var totalDelta = kernelDelta + userDelta;
        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        if (totalDelta == 0)
        {
            samples.Add(MetricSample.Missing(
                WellKnownMetrics.CpuTotalUtilization,
                timestampUtc,
                Source,
                MetricAvailability.Stale,
                MetricUnavailableReason.InvalidData,
                "The system-time counters did not advance."));
            return;
        }

        var utilization = 100d * (totalDelta - Math.Min(idleDelta, totalDelta)) / totalDelta;
        samples.Add(MetricSample.Available(
            WellKnownMetrics.CpuTotalUtilization,
            Math.Clamp(utilization, 0d, 100d),
            timestampUtc,
            Source));
    }

    private static void ReadMemory(
        DateTimeOffset timestampUtc,
        List<MetricSample> samples,
        List<string> diagnostics)
    {
        var memory = new NativeMethods.MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>()
        };
        if (!NativeMethods.GlobalMemoryStatusEx(ref memory))
        {
            var message = GetLastNativeError("GlobalMemoryStatusEx");
            diagnostics.Add(message);
            foreach (var metricId in new[]
                     {
                         WellKnownMetrics.MemoryUsedBytes,
                         WellKnownMetrics.MemoryAvailableBytes,
                         WellKnownMetrics.MemoryTotalBytes,
                         WellKnownMetrics.MemoryUtilization
                     })
            {
                samples.Add(MetricSample.Missing(
                    metricId,
                    timestampUtc,
                    Source,
                    MetricAvailability.Error,
                    MetricUnavailableReason.ProviderFaulted,
                    message));
            }

            return;
        }

        var used = memory.TotalPhysical - memory.AvailablePhysical;
        samples.Add(MetricSample.Available(
            WellKnownMetrics.MemoryUsedBytes,
            used,
            timestampUtc,
            Source));
        samples.Add(MetricSample.Available(
            WellKnownMetrics.MemoryAvailableBytes,
            memory.AvailablePhysical,
            timestampUtc,
            Source));
        samples.Add(MetricSample.Available(
            WellKnownMetrics.MemoryTotalBytes,
            memory.TotalPhysical,
            timestampUtc,
            Source));
        samples.Add(MetricSample.Available(
            WellKnownMetrics.MemoryUtilization,
            memory.TotalPhysical == 0 ? 0 : 100d * used / memory.TotalPhysical,
            timestampUtc,
            Source));
    }

    private void ReadNetwork(
        DateTimeOffset timestampUtc,
        List<MetricSample> samples,
        List<string> diagnostics)
    {
        var current = new Dictionary<string, NetworkCounters>(StringComparer.Ordinal);
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback
                        or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                try
                {
                    var counters = adapter.GetIPStatistics();
                    current[adapter.Id] = new NetworkCounters(
                        counters.BytesReceived,
                        counters.BytesSent);
                }
                catch (NetworkInformationException)
                {
                    // Interfaces can disappear during enumeration; other active interfaces remain valid.
                }
            }
        }
        catch (NetworkInformationException exception)
        {
            diagnostics.Add($"Network counters: {exception.Message}");
        }

        var timestamp = Stopwatch.GetTimestamp();
        if (_previousNetworkTimestamp == 0)
        {
            ReplaceNetworkBaseline(current, timestamp);
            AddNetworkPendingSamples(timestampUtc, samples);
            return;
        }

        long receivedDelta = 0;
        long sentDelta = 0;
        var matchingAdapters = 0;
        foreach (var pair in current)
        {
            if (!_previousNetwork.TryGetValue(pair.Key, out var previous) ||
                pair.Value.Received < previous.Received ||
                pair.Value.Sent < previous.Sent)
            {
                continue;
            }

            receivedDelta += pair.Value.Received - previous.Received;
            sentDelta += pair.Value.Sent - previous.Sent;
            matchingAdapters++;
        }

        var elapsedSeconds =
            (timestamp - _previousNetworkTimestamp) / (double)Stopwatch.Frequency;
        ReplaceNetworkBaseline(current, timestamp);

        if (matchingAdapters == 0 || elapsedSeconds <= 0)
        {
            AddNetworkPendingSamples(timestampUtc, samples);
            return;
        }

        samples.Add(MetricSample.Available(
            WellKnownMetrics.NetworkDownloadRate,
            Math.Max(0, receivedDelta / elapsedSeconds),
            timestampUtc,
            Source));
        samples.Add(MetricSample.Available(
            WellKnownMetrics.NetworkUploadRate,
            Math.Max(0, sentDelta / elapsedSeconds),
            timestampUtc,
            Source));
    }

    private static void ReadUptime(
        DateTimeOffset timestampUtc,
        List<MetricSample> samples) =>
        samples.Add(MetricSample.Available(
            WellKnownMetrics.SystemUptime,
            Math.Max(0, Environment.TickCount64 / 1000d),
            timestampUtc,
            Source));

    private static void ReadBattery(
        DateTimeOffset timestampUtc,
        List<MetricSample> samples,
        List<string> diagnostics)
    {
        if (!NativeMethods.GetSystemPowerStatus(out var power))
        {
            var message = GetLastNativeError("GetSystemPowerStatus");
            diagnostics.Add(message);
            AddBatteryMissing(
                timestampUtc,
                samples,
                MetricAvailability.Error,
                MetricUnavailableReason.ProviderFaulted,
                message);
            return;
        }

        const byte noBattery = 128;
        if ((power.BatteryFlag & noBattery) != 0)
        {
            AddBatteryMissing(
                timestampUtc,
                samples,
                MetricAvailability.Unavailable,
                MetricUnavailableReason.HardwareNotPresent,
                "Windows reports no system battery.");
            return;
        }

        if (power.BatteryLifePercent == byte.MaxValue)
        {
            samples.Add(MetricSample.Missing(
                WellKnownMetrics.BatteryCharge,
                timestampUtc,
                Source,
                MetricAvailability.Unavailable,
                MetricUnavailableReason.InvalidData,
                "Battery charge is unknown."));
        }
        else
        {
            samples.Add(MetricSample.Available(
                WellKnownMetrics.BatteryCharge,
                Math.Clamp(power.BatteryLifePercent, (byte)0, (byte)100),
                timestampUtc,
                Source));
        }

        samples.Add(MetricSample.Available(
            WellKnownMetrics.BatteryAcOnline,
            power.AcLineStatus == 1 ? 1 : 0,
            timestampUtc,
            Source));
        samples.Add(MetricSample.Available(
            WellKnownMetrics.BatterySaver,
            power.SystemStatusFlag == 1 ? 1 : 0,
            timestampUtc,
            Source));

        if (power.BatteryLifeTime == uint.MaxValue)
        {
            samples.Add(MetricSample.Missing(
                WellKnownMetrics.BatteryRemaining,
                timestampUtc,
                Source,
                MetricAvailability.Unavailable,
                MetricUnavailableReason.InvalidData,
                "Battery time remaining is unknown."));
        }
        else
        {
            samples.Add(MetricSample.Available(
                WellKnownMetrics.BatteryRemaining,
                power.BatteryLifeTime,
                timestampUtc,
                Source));
        }
    }

    private static ProviderPollResult Unsupported(DateTimeOffset timestampUtc) =>
        new()
        {
            Samples = MetricDescriptors
                .Select(descriptor => MetricSample.Missing(
                    descriptor.Id,
                    timestampUtc,
                    Source,
                    MetricAvailability.Unavailable,
                    MetricUnavailableReason.ProviderNotSupported,
                    "This provider requires Windows."))
                .ToArray(),
            HealthState = ProviderHealthState.Unavailable,
            Reason = MetricUnavailableReason.ProviderNotSupported,
            Message = "Windows native metrics are not supported on this operating system."
        };

    private void ReplaceNetworkBaseline(
        Dictionary<string, NetworkCounters> current,
        long timestamp)
    {
        _previousNetwork.Clear();
        foreach (var pair in current)
        {
            _previousNetwork[pair.Key] = pair.Value;
        }

        _previousNetworkTimestamp = timestamp;
    }

    private static void AddNetworkPendingSamples(
        DateTimeOffset timestampUtc,
        List<MetricSample> samples)
    {
        samples.Add(MetricSample.Missing(
            WellKnownMetrics.NetworkDownloadRate,
            timestampUtc,
            Source,
            MetricAvailability.Initializing,
            MetricUnavailableReason.FirstSamplePending,
            "A second network-counter sample is required."));
        samples.Add(MetricSample.Missing(
            WellKnownMetrics.NetworkUploadRate,
            timestampUtc,
            Source,
            MetricAvailability.Initializing,
            MetricUnavailableReason.FirstSamplePending,
            "A second network-counter sample is required."));
    }

    private static void AddBatteryMissing(
        DateTimeOffset timestampUtc,
        List<MetricSample> samples,
        MetricAvailability availability,
        MetricUnavailableReason reason,
        string message)
    {
        foreach (var metricId in new[]
                 {
                     WellKnownMetrics.BatteryCharge,
                     WellKnownMetrics.BatteryAcOnline,
                     WellKnownMetrics.BatteryRemaining,
                     WellKnownMetrics.BatterySaver
                 })
        {
            samples.Add(MetricSample.Missing(
                metricId,
                timestampUtc,
                Source,
                availability,
                reason,
                message));
        }
    }

    private static string GetLastNativeError(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return error == 0
            ? $"{operation} failed."
            : $"{operation}: {new Win32Exception(error).Message}";
    }

    private readonly record struct NetworkCounters(long Received, long Sent);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(
            out FileTime idleTime,
            out FileTime kernelTime,
            out FileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct FileTime
        {
            internal readonly uint Low;
            internal readonly uint High;
            internal ulong Value => ((ulong)High << 32) | Low;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryStatusEx
        {
            internal uint Length;
            internal uint MemoryLoad;
            internal ulong TotalPhysical;
            internal ulong AvailablePhysical;
            internal ulong TotalPageFile;
            internal ulong AvailablePageFile;
            internal ulong TotalVirtual;
            internal ulong AvailableVirtual;
            internal ulong AvailableExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct SystemPowerStatus
        {
            internal readonly byte AcLineStatus;
            internal readonly byte BatteryFlag;
            internal readonly byte BatteryLifePercent;
            internal readonly byte SystemStatusFlag;
            internal readonly uint BatteryLifeTime;
            internal readonly uint BatteryFullLifeTime;
        }
    }
}
