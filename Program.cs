using Windows.Devices.Bluetooth;
using AccessoryBatteryTray;

if (args is ["--watch"])
    return await BluetoothConnectionMonitor.RunAsync();

if (args is not ["--diagnose"])
{
    Console.WriteLine("Usage: AccessoryBatteryTray --diagnose | --watch");
    return 0;
}

var classic = await BluetoothDiagnostics.RunAsync(
    "Classic",
    BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
    BluetoothDevice.GetDeviceSelectorFromPairingState(true));

var lowEnergy = await BluetoothDiagnostics.RunAsync(
    "LE",
    BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
    BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
    BluetoothDiagnostics.ReadBatteryAsync);

return classic && lowEnergy ? 0 : 1;
