using AccessoryBatteryTray.Features.Bluetooth.Diagnose;
using AccessoryBatteryTray.Features.Bluetooth.Watch;
using AccessoryBatteryTray.Features.Tray;
using AccessoryBatteryTray.Features.Tray.Diagnostics;

internal static class Program
{
    private const string DiagnoseCommand = "--diagnose";
    private const string DiagnoseIconsCommand = "--diagnose-icons";
    private const string WatchCommand = "--watch";
    private const string TrayCommand = "--tray";
    private const string TrayPreviewCommand = "--tray-preview";
    private const string TrayIconPreviewCommand = "--tray-icon-preview";

    [STAThread]
    private static int Main(string[] args)
    {
        switch (args)
        {
            case [DiagnoseCommand]:
                return BluetoothDiagnostics.RunAsync().GetAwaiter().GetResult();
            case [DiagnoseIconsCommand]:
                return DeviceIconDiagnostics.RunAsync().GetAwaiter().GetResult();
            case [WatchCommand]:
                return BluetoothWatchConsole.RunAsync().GetAwaiter().GetResult();
            case [TrayCommand]:
                return TrayApplication.RunLive();
            case [TrayPreviewCommand]:
                return TrayApplication.RunPreview();
            case [TrayIconPreviewCommand]:
                return TrayApplication.RunIconPreview();
            default:
                Console.WriteLine(
                    $"Usage: AccessoryBatteryTray {DiagnoseCommand} | {DiagnoseIconsCommand} | {WatchCommand} | {TrayCommand} | {TrayPreviewCommand} | {TrayIconPreviewCommand}");
                return 0;
        }
    }
}
