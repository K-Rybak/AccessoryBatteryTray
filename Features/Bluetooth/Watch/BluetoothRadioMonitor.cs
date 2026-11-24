using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace AccessoryBatteryTray.Features.Bluetooth.Watch;

internal static class BluetoothRadioMonitor
{
    public static async Task<int> RunAsync(Func<CancellationToken, Task<int>> watchConnections, CancellationToken shutdown)
    {
        var radio = await FindRadioAsync();
        if (radio is null)
            return await watchConnections(shutdown);

        while (await WaitForPowerAsync(radio, poweredOn: true, shutdown))
        {
            using var session = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            var powerOff = WaitForPowerAsync(radio, poweredOn: false, session.Token);
            var watching = powerOff.IsCompletedSuccessfully && powerOff.Result
                ? Task.FromResult(0)
                : watchConnections(session.Token);

            await Task.WhenAny(watching, powerOff);
            var radioIsOff = !IsPoweredOn(radio);
            session.Cancel();
            var radioTurnedOff = await powerOff || radioIsOff;

            try
            {
                var exitCode = await watching;
                if (!radioTurnedOff)
                    return exitCode;
            }
            catch (Exception error) when (radioTurnedOff && IsRadioUnavailable(error))
            {
                // A watcher can become unavailable while the radio powers down.
            }
        }

        return 0;
    }

    private static bool IsPoweredOn(Radio radio) => radio.State == RadioState.On;

    private static async Task<bool> WaitForPowerAsync(Radio radio, bool poweredOn, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(Radio sender, object args)
        {
            if (IsPoweredOn(sender) == poweredOn)
                reached.TrySetResult();
        }

        radio.StateChanged += OnStateChanged;
        try
        {
            OnStateChanged(radio, EventArgs.Empty);
            await reached.Task.WaitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            radio.StateChanged -= OnStateChanged;
        }
    }

    private static async Task<Radio?> FindRadioAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is not null && await adapter.GetRadioAsync() is { } radio)
                return radio;
        }
        catch (Exception error) when (IsRadioUnavailable(error))
        {
            // The default adapter may be unavailable while Bluetooth is off.
        }

        try
        {
            var radios = await Radio.GetRadiosAsync();
            var bluetooth = radios.Where(radio => radio.Kind == RadioKind.Bluetooth).Take(2).ToArray();
            return bluetooth.Length == 1 ? bluetooth[0] : null;
        }
        catch (Exception error) when (IsRadioUnavailable(error))
        {
            return null;
        }
    }

    private static bool IsRadioUnavailable(Exception error) =>
        error is COMException or UnauthorizedAccessException or InvalidOperationException;
}
