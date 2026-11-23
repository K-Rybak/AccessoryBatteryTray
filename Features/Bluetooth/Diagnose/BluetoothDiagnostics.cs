using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace AccessoryBatteryTray.Features.Bluetooth.Diagnose;

internal static class BluetoothDiagnostics
{
    private const string IsConnected = "System.Devices.Aep.IsConnected";
    private static readonly string[] ConnectionProperties = [IsConnected];

    public static async Task<int> RunAsync()
    {
        var classic = await DiagnoseTransportAsync(
            "Classic",
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            BluetoothDevice.GetDeviceSelectorFromPairingState(true));

        var lowEnergy = await DiagnoseTransportAsync(
            "LE",
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
            GattBatterySource.ReadBatteryAsync);

        return classic && lowEnergy ? 0 : 1;
    }

    private static async Task<bool> DiagnoseTransportAsync(
        string transport,
        string connectedSelector,
        string pairedSelector,
        Func<string, Task<string>>? readBattery = null)
    {
        Console.WriteLine();
        Console.WriteLine($"[{transport}]");

        DeviceInformationCollection connected;
        DeviceInformationCollection paired;
        try
        {
            connected = await DeviceInformation.FindAllAsync(connectedSelector);
            paired = await DeviceInformation.FindAllAsync(pairedSelector, ConnectionProperties);
        }
        catch (Exception error)
        {
            Console.WriteLine($"Enumeration failed: {error.GetType().Name} (0x{error.HResult:X8}): {error.Message}");
            return false;
        }

        Console.WriteLine($"Connected selector matches: {connected.Count}");
        var connectedIds = connected.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var device in connected)
        {
            var result = readBattery is null
                ? "Battery unavailable through the Classic entry; check LE results"
                : await readBattery(device.Id);
            Console.WriteLine($"  {device.Name} | Connected at enumeration | {result}");
        }

        foreach (var device in paired.Where(device => !connectedIds.Contains(device.Id)))
        {
            var status = device.Properties.TryGetValue(IsConnected, out var value)
                ? value switch
                {
                    false => "Disconnected",
                    true => "Connection changed during enumeration; run again",
                    _ => "Connection status unavailable"
                }
                : "Connection status unavailable";
            Console.WriteLine($"  {device.Name} | Paired | {status} | Excluded by connected selector");
        }

        return true;
    }
}
