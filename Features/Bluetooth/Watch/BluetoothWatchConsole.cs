using AccessoryBatteryTray.Features.Bluetooth.Common;

namespace AccessoryBatteryTray.Features.Bluetooth.Watch;

internal static class BluetoothWatchConsole
{
    public static async Task<int> RunAsync()
    {
        using CancellationTokenSource? cancellation = new();
        ConsoleCancelEventHandler cancel = (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            Console.WriteLine("Watching Bluetooth connections. Press Ctrl+C to stop.");
            return await BluetoothConnectionMonitor.RunAsync(WriteEvent, cancellation.Token);
        }
        catch (Exception error)
        {
            await Console.Error.WriteLineAsync($"Could not start Bluetooth monitoring (0x{error.HResult:X8}): {error.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static void WriteEvent(BluetoothMonitorEvent update)
    {
        var message = update switch
        {
            BluetoothMonitorEvent.Connected connected => $"Connected: {connected.DeviceName}",
            BluetoothMonitorEvent.Disconnected disconnected => $"Disconnected: {disconnected.DeviceName}",
            BluetoothMonitorEvent.BatteryReported battery =>
                $"{battery.DeviceName} | {BatteryConsoleText.Format(battery.Report)}",
            BluetoothMonitorEvent.InitialEnumerationCompleted completed =>
                $"Initial enumeration complete: {completed.ConnectedCount} connected",
            BluetoothMonitorEvent.WatcherStopped stopped => $"Watcher stopped unexpectedly: {stopped.Status}",
            _ => throw new ArgumentOutOfRangeException(nameof(update))
        };
        var writer = update is BluetoothMonitorEvent.WatcherStopped ? Console.Error : Console.Out;
        writer.WriteLine($"[{update.Transport}] {message}");
    }
}
