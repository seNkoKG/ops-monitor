namespace OpsMonitor.Core.Platform;

/// <summary>
/// Owns a process-lifetime named mutex. The lease must be disposed on the same
/// thread that acquired it, which is naturally true for WPF startup and exit.
/// </summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private Mutex? _mutex;

    private SingleInstanceCoordinator(Mutex mutex) => _mutex = mutex;

    public static bool TryAcquire(
        string mutexName,
        out SingleInstanceCoordinator? coordinator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        var mutex = new Mutex(
            initiallyOwned: true,
            mutexName,
            out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            coordinator = null;
            return false;
        }

        coordinator = new SingleInstanceCoordinator(mutex);
        return true;
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        finally
        {
            mutex.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
