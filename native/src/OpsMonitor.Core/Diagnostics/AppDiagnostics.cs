using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace OpsMonitor.Core.Diagnostics;

/// <summary>
/// Process-local diagnostics backed by a small, bounded per-user log. Logging
/// is deliberately best-effort: a diagnostics failure must never take down the
/// monitor it is meant to diagnose.
/// </summary>
public static class AppDiagnostics
{
    private const long MaximumLogBytes = 512 * 1024;
    private const int RetainedLogCount = 2;
    private static readonly Lock Gate = new();
    private static RollingFileLog? _log;
    private static bool _traceListenerInstalled;

    public static string? CurrentLogPath
    {
        get
        {
            lock (Gate)
            {
                return _log?.LogPath;
            }
        }
    }

    public static void Initialize(string component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        lock (Gate)
        {
            if (_log is not null)
            {
                return;
            }

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OPS Monitor",
                "Logs");
            var fileName = SanitizeFileName(component) + ".log";
            _log = new RollingFileLog(
                Path.Combine(directory, fileName),
                MaximumLogBytes,
                RetainedLogCount);

            if (!_traceListenerInstalled)
            {
                Trace.Listeners.Add(new AppDiagnosticsTraceListener());
                Trace.AutoFlush = true;
                _traceListenerInstalled = true;
            }
        }

        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? assembly?.GetName().Version?.ToString() ?? "unknown";
        Information(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Process started. version={version}; pid={Environment.ProcessId}; os={Environment.OSVersion.VersionString}"));
    }

    public static void Information(string message) => Write("INFO", message);

    public static void Warning(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write("ERROR", $"{context}{Environment.NewLine}{exception}");
    }

    public static void Shutdown(int exitCode)
    {
        Information(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Process exiting. code={exitCode}"));
    }

    private static void Write(string level, string? message)
    {
        RollingFileLog? log;
        lock (Gate)
        {
            log = _log;
        }

        log?.Write(level, message ?? string.Empty);
    }

    private static string SanitizeFileName(string component)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = component
            .Trim()
            .ToLowerInvariant()
            .Select(character => invalid.Contains(character) || char.IsWhiteSpace(character)
                ? '-'
                : character)
            .ToArray();
        var result = new string(characters).Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "ops-monitor" : result;
    }

    private sealed class AppDiagnosticsTraceListener : TraceListener
    {
        public override void Write(string? message) =>
            AppDiagnostics.Information(message ?? string.Empty);

        public override void WriteLine(string? message) =>
            AppDiagnostics.Information(message ?? string.Empty);

        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? message)
        {
            _ = eventCache;
            _ = id;
            AppDiagnostics.Write(
                eventType is TraceEventType.Critical or TraceEventType.Error
                    ? "ERROR"
                    : eventType is TraceEventType.Warning
                        ? "WARN"
                        : "INFO",
                $"{source}: {message}");
        }

        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? format,
            params object?[]? args)
        {
            string message;
            try
            {
                message = args is { Length: > 0 }
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        format ?? string.Empty,
                        args)
                    : format ?? string.Empty;
            }
            catch (FormatException)
            {
                message = format ?? string.Empty;
            }

            TraceEvent(eventCache, source, eventType, id, message);
        }
    }
}

internal sealed class RollingFileLog
{
    private const int MaximumEntryCharacters = 64 * 1024;
    private readonly Lock _gate = new();
    private readonly long _maximumBytes;
    private readonly int _retainedCount;
    private readonly UTF8Encoding _encoding = new(encoderShouldEmitUTF8Identifier: false);

    internal RollingFileLog(string logPath, long maximumBytes, int retainedCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 4 * 1024);
        ArgumentOutOfRangeException.ThrowIfNegative(retainedCount);

        LogPath = Path.GetFullPath(logPath);
        _maximumBytes = maximumBytes;
        _retainedCount = retainedCount;
    }

    internal string LogPath { get; }

    internal void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }

                Directory.CreateDirectory(directory);
                var normalized = NormalizeMessage(message);
                var timestamp = DateTimeOffset.UtcNow.ToString(
                    "yyyy-MM-ddTHH:mm:ss.fffZ",
                    CultureInfo.InvariantCulture);
                var entry = $"{timestamp} [{level}] {normalized}{Environment.NewLine}";
                var entryBytes = _encoding.GetByteCount(entry);
                var currentBytes = File.Exists(LogPath)
                    ? new FileInfo(LogPath).Length
                    : 0;
                if (currentBytes + entryBytes > _maximumBytes)
                {
                    Rotate();
                }

                File.AppendAllText(LogPath, entry, _encoding);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            // Diagnostics are best-effort by design.
        }
    }

    private static string NormalizeMessage(string message)
    {
        var limited = message.Length <= MaximumEntryCharacters
            ? message
            : message[..MaximumEntryCharacters] + "… [truncated]";
        return limited
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", Environment.NewLine + "    ", StringComparison.Ordinal);
    }

    private void Rotate()
    {
        if (_retainedCount == 0)
        {
            File.Delete(LogPath);
            return;
        }

        for (var index = _retainedCount; index >= 1; index--)
        {
            var destination = BackupPath(index);
            var source = index == 1 ? LogPath : BackupPath(index - 1);
            if (!File.Exists(source))
            {
                continue;
            }

            File.Move(source, destination, overwrite: true);
        }
    }

    private string BackupPath(int index) =>
        Path.Combine(
            Path.GetDirectoryName(LogPath)!,
            $"{Path.GetFileNameWithoutExtension(LogPath)}.{index}{Path.GetExtension(LogPath)}");
}
