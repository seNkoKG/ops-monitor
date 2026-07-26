using System.Diagnostics;

namespace OpsMonitor.Core.Settings;

internal sealed class CrossProcessFileLock
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private readonly string _lockPath;

    internal CrossProcessFileLock(string protectedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPath);
        _lockPath = Path.GetFullPath(protectedPath) + ".lock";
    }

    internal async ValueTask<FileStream> AcquireAsync(
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_lockPath) ??
                        throw new InvalidOperationException(
                            "The settings lock path has no parent directory.");
        Directory.CreateDirectory(directory);

        var timer = Stopwatch.StartNew();
        IOException? lastError = null;
        while (timer.Elapsed < DefaultTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException exception)
            {
                lastError = exception;
            }

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new IOException(
            $"Timed out waiting for the settings writer lock '{_lockPath}'.",
            lastError);
    }
}
