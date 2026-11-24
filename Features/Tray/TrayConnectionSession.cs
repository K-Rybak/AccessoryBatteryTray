using System.Runtime.InteropServices;
using AccessoryBatteryTray.Features.Bluetooth;
using AccessoryBatteryTray.Features.Bluetooth.Watch;

namespace AccessoryBatteryTray.Features.Tray;

internal sealed class TrayConnectionSession(AccessoryPanel panel)
{
    private CancellationTokenSource? _activeSession;
    private TaskCompletionSource? _resumeSignal;

    public void Suspend()
    {
        _resumeSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        InvalidateConnections();
    }

    public void Resume()
    {
        InvalidateConnections();
        var resumeSignal = _resumeSignal;
        _resumeSignal = null;
        resumeSignal?.TrySetResult();
    }

    public async Task<int> RunAsync(CancellationToken shutdown)
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                if (_resumeSignal is { } suspended)
                {
                    await suspended.Task.WaitAsync(shutdown);
                    continue;
                }

                using var session = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
                _activeSession = session;
                var sessionToken = session.Token;
                try
                {
                    var result = await BluetoothConnectionMonitor.RunAsync(
                        update => PostUpdate(update, sessionToken), sessionToken);
                    if (!sessionToken.IsCancellationRequested)
                        return result;
                }
                catch (Exception error) when (sessionToken.IsCancellationRequested &&
                    error is (OperationCanceledException or COMException or InvalidOperationException
                        or UnauthorizedAccessException))
                {
                    // The Bluetooth APIs can stop responding while Windows suspends the adapter.
                }
                finally
                {
                    _activeSession = null;
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }

        return 0;
    }

    private void InvalidateConnections()
    {
        _activeSession?.Cancel();
        panel.ClearDevices();
    }

    private void PostUpdate(BluetoothMonitorEvent update, CancellationToken sessionToken)
    {
        if (sessionToken.IsCancellationRequested)
            return;

        panel.Post(() =>
        {
            if (sessionToken.IsCancellationRequested)
                return;

            switch (update)
            {
                case BluetoothMonitorEvent.BatteryReported { Report: BatteryReport.Reading reading } battery:
                    panel.SetDevice(battery.DeviceId, reading.Kind, battery.DeviceName, reading.Percent, useSystemGlyph: true);
                    break;
                case BluetoothMonitorEvent.Disconnected disconnected:
                    panel.RemoveDevice(disconnected.DeviceId);
                    break;
                case BluetoothMonitorEvent.WatcherStopped stopped:
                    Console.Error.WriteLine($"[{stopped.Transport}] Watcher stopped unexpectedly: {stopped.Status}");
                    break;
            }
        });
    }
}
