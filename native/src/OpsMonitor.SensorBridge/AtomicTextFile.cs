using System.Text;

namespace OpsMonitor.SensorBridge;

internal static class AtomicTextFile
{
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    internal static async Task WriteAsync(
        string destinationPath,
        string contents,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(contents);

        string fullPath = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "The destination must include a directory.",
                nameof(destinationPath));
        }

        for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                await WriteOnceAsync(
                        fullPath,
                        directory,
                        contents,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (
                attempt < MaximumAttempts &&
                exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(RetryDelay * attempt, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteOnceAsync(
        string fullPath,
        string directory,
        string contents,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4_096,
                             FileOptions.Asynchronous))
            {
                byte[] bytes = Utf8WithoutBom.GetBytes(contents);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (SensorFailure.IsExpected(exception))
            {
                // A failed cleanup is harmless and must not mask the write result.
            }
        }
    }
}
