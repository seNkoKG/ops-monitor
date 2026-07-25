using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PerformancePill.SensorBridge
{
    internal static class Program
    {
        private const string MutexName = "Global\\PerformancePillCpuTemperatureBridge";
        private static readonly string DataDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PerformancePill");
        private static readonly string DataPath = Path.Combine(DataDirectory, "cpu-temperature.txt");
        private static readonly string DiagnosticPath = Path.Combine(DataDirectory, "cpu-temperature-diagnostic.txt");
        private static readonly string RyzenCli = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "AMD", "RyzenMasterSDK", "AMDRyzenMasterCLI", "bin-prebuilt", "AMDRyzenMasterCLI.exe");

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [STAThread]
        private static int Main()
        {
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                    return 0;

                Directory.CreateDirectory(DataDirectory);
                var iteration = 0;
                var missingWidgetChecks = 0;
                while (true)
                {
                    double temperature;
                    if (TryReadRyzenTemperature(out temperature))
                        WriteReading(temperature);

                    iteration++;
                    if (iteration % 4 == 0)
                    {
                        missingWidgetChecks = IsWidgetRunning() ? 0 : missingWidgetChecks + 1;
                        if (missingWidgetChecks >= 3)
                            return 0;
                    }
                    Thread.Sleep(5000);
                }
            }
        }

        private static bool IsWidgetRunning()
        {
            return FindWindow(null, "Performance Pill") != IntPtr.Zero;
        }

        private static bool TryReadRyzenTemperature(out double temperature)
        {
            temperature = -1;
            if (!File.Exists(RyzenCli))
                return false;

            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = RyzenCli,
                    Arguments = "-a GetPMTableData",
                    WorkingDirectory = Path.GetDirectoryName(RyzenCli),
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
                        if (!process.WaitForExit(8000))
                        {
                            try { process.Kill(); } catch { }
                            try { process.WaitForExit(1000); } catch { }
                            WriteDiagnostic("AMD sensor command timed out.");
                            return false;
                        }
                        if (!Task.WaitAll(new Task[] { outputTask, errorTask }, 1500))
                        {
                            WriteDiagnostic("AMD sensor output did not close cleanly.");
                            return false;
                        }

                        var combined = outputTask.Result + Environment.NewLine + errorTask.Result;
                        var match = Regex.Match(
                            combined,
                            @"cHTC\s+Current\s+Value\s*:\s*(-?[0-9]+(?:\.[0-9]+)?)\s*Celsius",
                            RegexOptions.IgnoreCase);
                        if (!match.Success)
                        {
                            match = Regex.Match(
                                combined,
                                @"(?:CPU\s+)?Temperature\s*:\s*(-?[0-9]+(?:\.[0-9]+)?)\s*Celsius",
                                RegexOptions.IgnoreCase);
                        }
                        if (!match.Success ||
                            !Double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out temperature))
                        {
                            WriteDiagnostic("Exit " + process.ExitCode + Environment.NewLine + combined);
                            return false;
                        }

                        return temperature >= 5 && temperature <= 125;
                    }
                }
            }
            catch (Exception error)
            {
                WriteDiagnostic(error.ToString());
                return false;
            }
        }

        private static void WriteDiagnostic(string value)
        {
            try { File.WriteAllText(DiagnosticPath, value); } catch { }
        }

        private static void WriteReading(double temperature)
        {
            var temporary = DataPath + ".tmp";
            var value = temperature.ToString("0.0", CultureInfo.InvariantCulture) + "|" +
                        DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
            File.WriteAllText(temporary, value);
            File.Copy(temporary, DataPath, true);
            File.Delete(temporary);
            if (File.Exists(DiagnosticPath))
                File.Delete(DiagnosticPath);
        }
    }
}
