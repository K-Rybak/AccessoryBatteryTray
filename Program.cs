using AccessoryBatteryTray.Features.Bluetooth.Diagnose;
using AccessoryBatteryTray.Features.Bluetooth.Watch;

const string diagnoseCommand = "--diagnose";
const string watchCommand = "--watch";

if (args is [watchCommand])
    return await BluetoothWatchConsole.RunAsync();

if (args is not [diagnoseCommand])
{
    Console.WriteLine($"Usage: AccessoryBatteryTray {diagnoseCommand} | {watchCommand}");
    return 0;
}

return await BluetoothDiagnostics.RunAsync();
