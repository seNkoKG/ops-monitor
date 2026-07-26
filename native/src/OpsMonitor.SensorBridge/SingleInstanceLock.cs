namespace OpsMonitor.SensorBridge;

internal sealed class SingleInstanceLock : IDisposable
{
    private const string MutexName = @"Global\OpsMonitor.SensorBridge";
    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceLock(Mutex mutex) => _mutex = mutex;

    internal static SingleInstanceLock? TryAcquire()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }

            return new SingleInstanceLock(mutex);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
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
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // The process is already shutting down or ownership was lost.
        }

        _mutex.Dispose();
    }
}
