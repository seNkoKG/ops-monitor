using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PerformancePill.Runtime
{
    public sealed class MetricSnapshot
    {
        public double CpuLoad { get; set; }
        public double CpuTemperature { get; set; }
        public double GpuLoad { get; set; }
        public double GpuTemperature { get; set; }
        public double RamUsedBytes { get; set; }
        public double RamTotalBytes { get; set; }
        public double DownloadBytesPerSecond { get; set; }
        public double UploadBytesPerSecond { get; set; }
        public double PingMilliseconds { get; set; }
        public double PacketLossPercent { get; set; }
        public int SignalPercent { get; set; }
        public string WifiName { get; set; }

        public MetricSnapshot()
        {
            CpuTemperature = -1;
            GpuLoad = -1;
            GpuTemperature = -1;
            PingMilliseconds = -1;
            PacketLossPercent = -1;
            SignalPercent = -1;
            WifiName = String.Empty;
        }
    }

    public static class MetricCollector
    {
        private static readonly object Sync = new object();
        private static ulong _previousIdle;
        private static ulong _previousKernel;
        private static ulong _previousUser;
        private static long _previousReceived;
        private static long _previousSent;
        private static long _previousNetworkStamp;
        private static DateTime _lastHardwarePoll = DateTime.MinValue;
        private static readonly Queue<bool> PingWindow = new Queue<bool>();
        private static double _cachedCpuTemperature = -1;
        private static double _cachedGpuTemperature = -1;
        private static double _cachedGpuLoad = -1;

        public static Task<MetricSnapshot> CollectAsync()
        {
            return Task.Run(() => Collect());
        }

        public static MetricSnapshot Collect()
        {
            lock (Sync)
            {
                var result = new MetricSnapshot();
                result.CpuLoad = ReadCpuLoad();
                ReadMemory(result);
                ReadNetworkRate(result);

                var now = DateTime.UtcNow;
                if ((now - _lastHardwarePoll).TotalSeconds >= 4)
                {
                    _lastHardwarePoll = now;
                    double cpuTemperature;
                    double gpuTemperature;
                    ReadHardwareTemperatures(out cpuTemperature, out gpuTemperature);

                    double vendorLoad;
                    double vendorTemperature;
                    if (ReadNvidia(out vendorLoad, out vendorTemperature))
                    {
                        _cachedGpuLoad = vendorLoad;
                        if (vendorTemperature > 0)
                            gpuTemperature = vendorTemperature;
                    }
                    else
                    {
                        _cachedGpuLoad = ReadWindowsGpuLoad();
                    }

                    _cachedCpuTemperature = cpuTemperature;
                    _cachedGpuTemperature = gpuTemperature;
                }

                result.CpuTemperature = _cachedCpuTemperature;
                result.GpuLoad = _cachedGpuLoad;
                result.GpuTemperature = _cachedGpuTemperature;
                double packetLoss;
                result.PingMilliseconds = ReadPing(out packetLoss);
                result.PacketLossPercent = packetLoss;
                return result;
            }
        }

        private static double ReadCpuLoad()
        {
            ulong idle;
            ulong kernel;
            ulong user;
            if (!NativeMethods.GetSystemTimes(out idle, out kernel, out user))
                return 0;

            if (_previousKernel == 0)
            {
                _previousIdle = idle;
                _previousKernel = kernel;
                _previousUser = user;
                return 0;
            }

            var idleDelta = idle - _previousIdle;
            var totalDelta = (kernel - _previousKernel) + (user - _previousUser);
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            if (totalDelta == 0)
                return 0;

            return Math.Max(0, Math.Min(100, 100.0 * (totalDelta - idleDelta) / totalDelta));
        }

        private static void ReadMemory(MetricSnapshot result)
        {
            var status = new NativeMethods.MemoryStatusEx();
            if (NativeMethods.GlobalMemoryStatusEx(status))
            {
                result.RamTotalBytes = status.TotalPhysical;
                result.RamUsedBytes = status.TotalPhysical - status.AvailablePhysical;
            }
        }

        private static void ReadNetworkRate(MetricSnapshot result)
        {
            long received = 0;
            long sent = 0;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    var stats = adapter.GetIPv4Statistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }
                catch
                {
                    // An adapter may disappear while Windows is enumerating it.
                }
            }

            var stamp = Stopwatch.GetTimestamp();
            if (_previousNetworkStamp != 0 && received >= _previousReceived && sent >= _previousSent)
            {
                var seconds = (stamp - _previousNetworkStamp) / (double)Stopwatch.Frequency;
                if (seconds > 0)
                {
                    result.DownloadBytesPerSecond = (received - _previousReceived) / seconds;
                    result.UploadBytesPerSecond = (sent - _previousSent) / seconds;
                }
            }

            _previousReceived = received;
            _previousSent = sent;
            _previousNetworkStamp = stamp;
        }

        private static double ReadPing(out double packetLossPercent)
        {
            var success = false;
            var roundTrip = -1.0;
            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send("1.1.1.1", 650);
                    success = reply != null && reply.Status == IPStatus.Success;
                    if (success)
                        roundTrip = reply.RoundtripTime;
                }
            }
            catch
            {
                success = false;
            }

            PingWindow.Enqueue(success);
            while (PingWindow.Count > 20)
                PingWindow.Dequeue();
            packetLossPercent = PingWindow.Count == 0
                ? -1
                : 100.0 * PingWindow.Count(item => !item) / PingWindow.Count;
            return roundTrip;
        }

        private static bool ReadNvidia(out double load, out double temperature)
        {
            load = -1;
            temperature = -1;
            try
            {
                var executable = System.IO.Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
                if (!System.IO.File.Exists(executable))
                    return false;

                var start = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(start))
                {
                    if (process == null)
                        return false;
                    using (var outputReader = process.StandardOutput)
                    using (var errorReader = process.StandardError)
                    {
                        var outputTask = outputReader.ReadToEndAsync();
                        var errorTask = errorReader.ReadToEndAsync();
                        if (!process.WaitForExit(1800))
                        {
                            try { process.Kill(); } catch { }
                            try { process.WaitForExit(500); } catch { }
                            return false;
                        }
                        if (!Task.WaitAll(new Task[] { outputTask, errorTask }, 1000))
                            return false;

                        foreach (var line in outputTask.Result.Split(
                            new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var values = line.Split(',');
                            if (values.Length < 2)
                                continue;
                            double parsedLoad;
                            double parsedTemperature;
                            if (Double.TryParse(values[0].Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out parsedLoad))
                                load = Math.Max(load, parsedLoad);
                            if (Double.TryParse(values[1].Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out parsedTemperature))
                                temperature = Math.Max(temperature, parsedTemperature);
                        }
                    }
                }
                return load >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static double ReadWindowsGpuLoad()
        {
            try
            {
                var byEngine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                using (var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine"))
                using (var values = searcher.Get())
                {
                    foreach (ManagementObject item in values)
                    {
                        try
                        {
                            var name = Convert.ToString(item["Name"], CultureInfo.InvariantCulture) ?? String.Empty;
                            var engine = "other";
                            var marker = name.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
                            if (marker >= 0)
                                engine = name.Substring(marker);

                            double utilization;
                            if (!Double.TryParse(Convert.ToString(item["UtilizationPercentage"], CultureInfo.InvariantCulture),
                                NumberStyles.Float, CultureInfo.InvariantCulture, out utilization))
                                continue;

                            if (!byEngine.ContainsKey(engine))
                                byEngine[engine] = 0;
                            byEngine[engine] += utilization;
                        }
                        finally
                        {
                            item.Dispose();
                        }
                    }
                }
                return byEngine.Count == 0 ? -1 : Math.Min(100, byEngine.Values.Max());
            }
            catch
            {
                return -1;
            }
        }

        private static void ReadHardwareTemperatures(out double cpuTemperature, out double gpuTemperature)
        {
            cpuTemperature = ReadCpuTemperatureBridge();
            gpuTemperature = -1;
            var nvidiaPath = System.IO.Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            if (cpuTemperature >= 0 && System.IO.File.Exists(nvidiaPath))
                return;

            foreach (var scope in new[] { "root\\LibreHardwareMonitor", "root\\OpenHardwareMonitor" })
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher(
                        scope, "SELECT Name, Identifier, Parent, Value FROM Sensor WHERE SensorType='Temperature'"))
                    using (var sensors = searcher.Get())
                    {
                        foreach (ManagementObject sensor in sensors)
                        {
                            try
                            {
                                var name = (Convert.ToString(sensor["Name"], CultureInfo.InvariantCulture) ?? String.Empty).ToLowerInvariant();
                                var identifier = (Convert.ToString(sensor["Identifier"], CultureInfo.InvariantCulture) ?? String.Empty).ToLowerInvariant();
                                var parent = (Convert.ToString(sensor["Parent"], CultureInfo.InvariantCulture) ?? String.Empty).ToLowerInvariant();
                                double value;
                                if (!Double.TryParse(Convert.ToString(sensor["Value"], CultureInfo.InvariantCulture),
                                    NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 5 || value > 125)
                                    continue;

                                var identity = name + " " + identifier + " " + parent;
                                if (identity.Contains("gpu"))
                                    gpuTemperature = Math.Max(gpuTemperature, value);
                                else if (identity.Contains("cpu") || identity.Contains("package") || identity.Contains("core max"))
                                    cpuTemperature = Math.Max(cpuTemperature, value);
                            }
                            finally
                            {
                                sensor.Dispose();
                            }
                        }
                    }
                }
                catch
                {
                    // These optional providers only exist when a compatible monitor is running.
                }
            }

            if (cpuTemperature >= 0)
                return;

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                using (var zones = searcher.Get())
                {
                    foreach (ManagementObject zone in zones)
                    {
                        try
                        {
                            var raw = Convert.ToDouble(zone["CurrentTemperature"], CultureInfo.InvariantCulture);
                            var celsius = (raw / 10.0) - 273.15;
                            if (celsius >= 5 && celsius <= 125)
                                cpuTemperature = Math.Max(cpuTemperature, celsius);
                        }
                        finally
                        {
                            zone.Dispose();
                        }
                    }
                }
            }
            catch
            {
                // Many desktop firmware implementations do not publish an ACPI temperature.
            }
        }

        private static double ReadCpuTemperatureBridge()
        {
            try
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PerformancePill",
                    "cpu-temperature.txt");
                if (!System.IO.File.Exists(path))
                    return -1;

                var values = System.IO.File.ReadAllText(path).Split('|');
                if (values.Length != 2)
                    return -1;

                double temperature;
                long ticks;
                if (!Double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out temperature) ||
                    !Int64.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
                    return -1;

                var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
                return age.TotalSeconds <= 20 && temperature >= 5 && temperature <= 125
                    ? temperature
                    : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

            internal static bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user)
            {
                FileTime idleTime;
                FileTime kernelTime;
                FileTime userTime;
                var success = GetSystemTimes(out idleTime, out kernelTime, out userTime);
                idle = idleTime.Value;
                kernel = kernelTime.Value;
                user = userTime.Value;
                return success;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx status);

            [StructLayout(LayoutKind.Sequential)]
            private struct FileTime
            {
                public uint Low;
                public uint High;
                public ulong Value { get { return ((ulong)High << 32) | Low; } }
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            internal sealed class MemoryStatusEx
            {
                public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
                public uint MemoryLoad;
                public ulong TotalPhysical;
                public ulong AvailablePhysical;
                public ulong TotalPageFile;
                public ulong AvailablePageFile;
                public ulong TotalVirtual;
                public ulong AvailableVirtual;
                public ulong AvailableExtendedVirtual;
            }
        }

        private static class NativeWifi
        {
            private const int CurrentConnectionOpcode = 7;
            private const int InterfaceConnected = 1;

            [DllImport("wlanapi.dll")]
            private static extern int WlanOpenHandle(
                int clientVersion, IntPtr reserved, out int negotiatedVersion, out IntPtr clientHandle);

            [DllImport("wlanapi.dll")]
            private static extern int WlanEnumInterfaces(
                IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

            [DllImport("wlanapi.dll")]
            private static extern int WlanQueryInterface(
                IntPtr clientHandle, ref Guid interfaceGuid, int opcode, IntPtr reserved,
                out int dataSize, out IntPtr data, IntPtr opcodeValueType);

            [DllImport("wlanapi.dll")]
            private static extern void WlanFreeMemory(IntPtr memory);

            [DllImport("wlanapi.dll")]
            private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

            internal static int ReadSignal(out string wifiName)
            {
                wifiName = String.Empty;
                IntPtr handle = IntPtr.Zero;
                IntPtr list = IntPtr.Zero;
                try
                {
                    int negotiated;
                    if (WlanOpenHandle(2, IntPtr.Zero, out negotiated, out handle) != 0)
                        return -1;
                    if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0)
                        return -1;

                    var count = Marshal.ReadInt32(list, 0);
                    var itemSize = Marshal.SizeOf(typeof(WlanInterfaceInfo));
                    var offset = 8;
                    var best = -1;
                    for (var index = 0; index < count; index++)
                    {
                        var itemPointer = new IntPtr(list.ToInt64() + offset + (index * itemSize));
                        var item = (WlanInterfaceInfo)Marshal.PtrToStructure(itemPointer, typeof(WlanInterfaceInfo));
                        if (item.State != InterfaceConnected)
                            continue;

                        int dataSize;
                        IntPtr data;
                        if (WlanQueryInterface(handle, ref item.InterfaceGuid, CurrentConnectionOpcode,
                            IntPtr.Zero, out dataSize, out data, IntPtr.Zero) != 0)
                            continue;
                        try
                        {
                            var connection = (WlanConnectionAttributes)Marshal.PtrToStructure(
                                data, typeof(WlanConnectionAttributes));
                            if ((int)connection.Association.SignalQuality > best)
                            {
                                best = (int)connection.Association.SignalQuality;
                                wifiName = connection.Association.Ssid.ToText();
                            }
                        }
                        finally
                        {
                            WlanFreeMemory(data);
                        }
                    }
                    return best;
                }
                catch
                {
                    return -1;
                }
                finally
                {
                    if (list != IntPtr.Zero)
                        WlanFreeMemory(list);
                    if (handle != IntPtr.Zero)
                        WlanCloseHandle(handle, IntPtr.Zero);
                }
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct WlanInterfaceInfo
            {
                public Guid InterfaceGuid;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
                public string Description;
                public int State;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct WlanConnectionAttributes
            {
                public int State;
                public int ConnectionMode;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
                public string ProfileName;
                public WlanAssociationAttributes Association;
                public WlanSecurityAttributes Security;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct Dot11Ssid
            {
                public uint Length;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
                public byte[] Bytes;

                public string ToText()
                {
                    if (Bytes == null || Length == 0)
                        return String.Empty;
                    return Encoding.UTF8.GetString(Bytes, 0, (int)Math.Min(Length, (uint)Bytes.Length));
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct WlanAssociationAttributes
            {
                public Dot11Ssid Ssid;
                public int BssType;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
                public byte[] Bssid;
                public int PhyType;
                public uint PhyIndex;
                public uint SignalQuality;
                public uint RxRate;
                public uint TxRate;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct WlanSecurityAttributes
            {
                [MarshalAs(UnmanagedType.Bool)]
                public bool SecurityEnabled;
                [MarshalAs(UnmanagedType.Bool)]
                public bool OneXEnabled;
                public int AuthAlgorithm;
                public int CipherAlgorithm;
            }
        }
    }
}
