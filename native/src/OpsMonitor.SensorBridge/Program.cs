namespace OpsMonitor.SensorBridge;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (!SensorBridgeOptions.TryParse(args, out var options, out string error))
        {
            await SensorBridgeDiagnostics.TryWriteAsync(
                SensorBridgeOptions.DefaultDiagnosticPath,
                $"Configuration error{Environment.NewLine}{error}",
                CancellationToken.None).ConfigureAwait(false);
            return SensorBridgeExitCodes.InvalidArguments;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        using var bridgeLock = SingleInstanceLock.TryAcquire();
        if (bridgeLock is null)
        {
            return SensorBridgeExitCodes.AlreadyRunning;
        }

        try
        {
            var host = new SensorBridgeHost(options);
            return await host.RunAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return SensorBridgeExitCodes.Success;
        }
        catch (Exception exception) when (SensorFailure.IsExpected(exception))
        {
            await SensorBridgeDiagnostics.TryWriteAsync(
                options.DiagnosticPath,
                $"Bridge fault{Environment.NewLine}{exception.Message}",
                CancellationToken.None).ConfigureAwait(false);
            return SensorBridgeExitCodes.Faulted;
        }
    }
}
