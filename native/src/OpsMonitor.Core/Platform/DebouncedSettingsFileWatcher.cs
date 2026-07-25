using System.Security.Cryptography;

namespace OpsMonitor.Core.Platform;

[Flags]
public enum SettingsFileChangeKind
{
    None = 0,
    Changed = 1,
    Created = 2,
    Deleted = 4,
    Renamed = 8,
    Rescan = 16
}

public sealed record DebouncedSettingsFileWatcherOptions
{
    public TimeSpan DebounceInterval { get; init; } = TimeSpan.FromMilliseconds(350);
    public int ReadRetryCount { get; init; } = 4;
    public TimeSpan ReadRetryDelay { get; init; } = TimeSpan.FromMilliseconds(60);
}

public sealed class SettingsReloadRequestedEventArgs : EventArgs
{
    public SettingsReloadRequestedEventArgs(
        string settingsPath,
        SettingsFileChangeKind changeKind,
        bool fileExists,
        DateTimeOffset detectedUtc)
    {
        SettingsPath = settingsPath;
        ChangeKind = changeKind;
        FileExists = fileExists;
        DetectedUtc = detectedUtc;
    }

    public string SettingsPath { get; }
    public SettingsFileChangeKind ChangeKind { get; }
    public bool FileExists { get; }
    public DateTimeOffset DetectedUtc { get; }
}

public sealed class SettingsFileWatcherErrorEventArgs : EventArgs
{
    public SettingsFileWatcherErrorEventArgs(string operation, Exception exception)
    {
        Operation = operation;
        Exception = exception;
    }

    public string Operation { get; }
    public Exception Exception { get; }
}

/// <summary>
/// Watches one settings file, coalesces atomic replace/move/write event bursts,
/// and raises a reload request only when file content actually changes.
/// </summary>
public sealed class DebouncedSettingsFileWatcher : IAsyncDisposable
{
    private const string MissingFingerprint = "missing";

    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _processGate = new(1, 1);
    private readonly DebouncedSettingsFileWatcherOptions _options;
    private readonly Timer _debounceTimer;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _runCancellation;
    private SettingsFileChangeKind _pendingChanges;
    private string _lastFingerprint = MissingFingerprint;
    private int _suppressionDepth;
    private bool _refreshBaselineOnly;
    private bool _running;
    private bool _disposed;

    public DebouncedSettingsFileWatcher(
        string settingsPath,
        DebouncedSettingsFileWatcherOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        SettingsPath = Path.GetFullPath(settingsPath);
        _options = options ?? new DebouncedSettingsFileWatcherOptions();
        if (_options.DebounceInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The debounce interval must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(_options.ReadRetryCount, 0);
        if (_options.ReadRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The read retry delay cannot be negative.");
        }

        _debounceTimer = new Timer(
            static state => ((DebouncedSettingsFileWatcher)state!).QueueProcessing(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<SettingsReloadRequestedEventArgs>? ReloadRequested;
    public event EventHandler<SettingsFileWatcherErrorEventArgs>? WatcherError;

    public string SettingsPath { get; }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_running)
                {
                    return;
                }
            }

            var directory = Path.GetDirectoryName(SettingsPath) ??
                            throw new InvalidOperationException(
                                "The settings path has no parent directory.");
            Directory.CreateDirectory(directory);
            var initialFingerprint = await ReadFingerprintWithRetryAsync(cancellationToken)
                .ConfigureAwait(false);

            var watcher = new FileSystemWatcher(directory, Path.GetFileName(SettingsPath))
            {
                IncludeSubdirectories = false,
                InternalBufferSize = 8 * 1024,
                NotifyFilter =
                    NotifyFilters.FileName |
                    NotifyFilters.CreationTime |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Size
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnWatcherError;

            lock (_gate)
            {
                if (_disposed)
                {
                    watcher.Dispose();
                    throw new ObjectDisposedException(nameof(DebouncedSettingsFileWatcher));
                }

                _lastFingerprint = initialFingerprint;
                _pendingChanges = SettingsFileChangeKind.None;
                _refreshBaselineOnly = false;
                _runCancellation = new CancellationTokenSource();
                _watcher = watcher;
                _running = true;
                watcher.EnableRaisingEvents = true;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        FileSystemWatcher? watcher;
        CancellationTokenSource? runCancellation;
        try
        {
            lock (_gate)
            {
                if (!_running)
                {
                    return;
                }

                _running = false;
                _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                watcher = _watcher;
                _watcher = null;
                runCancellation = _runCancellation;
                _runCancellation = null;
                _pendingChanges = SettingsFileChangeKind.None;
                _refreshBaselineOnly = false;
                if (watcher is not null)
                {
                    watcher.EnableRaisingEvents = false;
                }
            }

            if (runCancellation is not null)
            {
                await runCancellation.CancelAsync().ConfigureAwait(false);
            }

            // Once shutdown has changed observable state, complete cleanup even
            // if the caller cancels. The in-flight reader already has the
            // cancelled per-run token and will exit promptly.
            await _processGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            _processGate.Release();

            if (watcher is not null)
            {
                watcher.Changed -= OnChanged;
                watcher.Created -= OnCreated;
                watcher.Deleted -= OnDeleted;
                watcher.Renamed -= OnRenamed;
                watcher.Error -= OnWatcherError;
                watcher.Dispose();
            }

            runCancellation?.Dispose();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Suppresses events while this process saves settings. Disposing the scope
    /// schedules a content baseline refresh, preventing the save from feeding
    /// back into a reload/save loop.
    /// </summary>
    public IDisposable SuppressNotifications()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _suppressionDepth++;
            _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        return new NotificationSuppression(this);
    }

    /// <summary>
    /// Updates the content baseline without raising ReloadRequested.
    /// </summary>
    public async Task AcknowledgeCurrentStateAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var fingerprint = await ReadFingerprintWithRetryAsync(cancellationToken)
            .ConfigureAwait(false);
        lock (_gate)
        {
            _lastFingerprint = fingerprint;
            _pendingChanges = SettingsFileChangeKind.None;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        await _debounceTimer.DisposeAsync().ConfigureAwait(false);
        _processGate.Dispose();
        _lifecycle.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnChanged(object sender, FileSystemEventArgs eventArgs) =>
        Schedule(SettingsFileChangeKind.Changed);

    private void OnCreated(object sender, FileSystemEventArgs eventArgs) =>
        Schedule(SettingsFileChangeKind.Created);

    private void OnDeleted(object sender, FileSystemEventArgs eventArgs) =>
        Schedule(SettingsFileChangeKind.Deleted);

    private void OnRenamed(object sender, RenamedEventArgs eventArgs) =>
        Schedule(SettingsFileChangeKind.Renamed);

    private void OnWatcherError(object sender, ErrorEventArgs eventArgs)
    {
        PublishError("FileSystemWatcher", eventArgs.GetException());
        Schedule(SettingsFileChangeKind.Rescan);
    }

    private void Schedule(SettingsFileChangeKind changeKind)
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _pendingChanges |= changeKind;
            if (_suppressionDepth > 0)
            {
                return;
            }

            _debounceTimer.Change(
                _options.DebounceInterval,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void QueueProcessing()
    {
        _ = ProcessDebouncedChangeAsync();
    }

    private async Task ProcessDebouncedChangeAsync()
    {
        CancellationToken cancellationToken;
        try
        {
            lock (_gate)
            {
                if (!_running || _runCancellation is null)
                {
                    return;
                }

                cancellationToken = _runCancellation.Token;
            }

            await _processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            SettingsFileChangeKind changes;
            bool baselineOnly;
            lock (_gate)
            {
                if (!_running)
                {
                    return;
                }

                changes = _pendingChanges;
                _pendingChanges = SettingsFileChangeKind.None;
                baselineOnly = _refreshBaselineOnly;
                _refreshBaselineOnly = false;
            }

            var fingerprint = await ReadFingerprintWithRetryAsync(cancellationToken)
                .ConfigureAwait(false);
            bool changed;
            lock (_gate)
            {
                if (!_running)
                {
                    return;
                }

                changed = !StringComparer.Ordinal.Equals(
                    fingerprint,
                    _lastFingerprint);
                _lastFingerprint = fingerprint;
            }

            if (!baselineOnly && changed)
            {
                PublishReloadRequested(new SettingsReloadRequestedEventArgs(
                    SettingsPath,
                    changes == SettingsFileChangeKind.None
                        ? SettingsFileChangeKind.Rescan
                        : changes,
                    fingerprint != MissingFingerprint,
                    DateTimeOffset.UtcNow));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal during StopAsync.
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                CryptographicException)
        {
            PublishError("Read settings fingerprint", exception);
        }
        finally
        {
            _processGate.Release();
        }
    }

    private async Task<string> ReadFingerprintWithRetryAsync(
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt <= _options.ReadRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return MissingFingerprint;
                }

                await using var stream = new FileStream(
                    SettingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = await SHA256.HashDataAsync(stream, cancellationToken)
                    .ConfigureAwait(false);
                return Convert.ToHexString(hash);
            }
            catch (FileNotFoundException)
            {
                return MissingFingerprint;
            }
            catch (DirectoryNotFoundException)
            {
                return MissingFingerprint;
            }
            catch (IOException exception)
            {
                lastError = exception;
            }
            catch (UnauthorizedAccessException exception)
            {
                lastError = exception;
            }

            if (attempt < _options.ReadRetryCount &&
                _options.ReadRetryDelay > TimeSpan.Zero)
            {
                await Task.Delay(_options.ReadRetryDelay, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        throw lastError ?? new IOException("Unable to read the settings file.");
    }

    private void ReleaseSuppression()
    {
        lock (_gate)
        {
            if (_suppressionDepth == 0)
            {
                return;
            }

            _suppressionDepth--;
            if (_suppressionDepth > 0 || !_running)
            {
                return;
            }

            _refreshBaselineOnly = true;
            _pendingChanges = SettingsFileChangeKind.None;
            _debounceTimer.Change(
                _options.DebounceInterval,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void PublishReloadRequested(SettingsReloadRequestedEventArgs eventArgs)
    {
        var handlers = ReloadRequested;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<SettingsReloadRequestedEventArgs> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                PublishError("ReloadRequested observer", exception);
            }
        }
    }

    private void PublishError(string operation, Exception exception)
    {
        var handlers = WatcherError;
        if (handlers is null)
        {
            return;
        }

        var eventArgs = new SettingsFileWatcherErrorEventArgs(operation, exception);
        foreach (EventHandler<SettingsFileWatcherErrorEventArgs> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch
            {
                // Diagnostics observers must never terminate the watcher.
            }
        }
    }

    private sealed class NotificationSuppression : IDisposable
    {
        private DebouncedSettingsFileWatcher? _owner;

        public NotificationSuppression(DebouncedSettingsFileWatcher owner) =>
            _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.ReleaseSuppression();
            GC.SuppressFinalize(this);
        }
    }
}
