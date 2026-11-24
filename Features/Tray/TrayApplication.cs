using System.Windows.Forms;
using AccessoryBatteryTray.Features.Bluetooth;
using AccessoryBatteryTray.Features.Bluetooth.Common;
using AccessoryBatteryTray.Features.Bluetooth.Watch;

namespace AccessoryBatteryTray.Features.Tray;

internal static class TrayApplication
{
    private const string InstanceMutexName = @"Local\AccessoryBatteryTray.LiveTray";

    public static int RunLive(bool consoleControls = true)
    {
        using var instanceMutex = new Mutex(initiallyOwned: false, name: InstanceMutexName);
        bool acquired;
        try
        {
            acquired = instanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            Console.WriteLine("AccessoryBatteryTray is already running in this Windows session.");
            return 0;
        }

        try
        {
            return Run(preview: false, consoleControls);
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }

    public static int RunPreview() => Run(preview: true, consoleControls: true);

    private static int Run(bool preview, bool consoleControls)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var panel = new AccessoryPanel();
        using var cancellation = new CancellationTokenSource();
        var exitCode = 0;

        void Publish(BluetoothMonitorEvent update)
        {
            switch (update)
            {
                case BluetoothMonitorEvent.BatteryReported { Report: BatteryReport.Reading reading } battery:
                    panel.Post(() => panel.SetDevice(
                        battery.DeviceId, reading.Kind, battery.DeviceName, reading.Percent, useSystemGlyph: true));
                    break;
                case BluetoothMonitorEvent.Disconnected disconnected:
                    panel.Post(() => panel.RemoveDevice(disconnected.DeviceId));
                    break;
                case BluetoothMonitorEvent.WatcherStopped stopped:
                    Console.Error.WriteLine($"[{stopped.Transport}] Watcher stopped unexpectedly: {stopped.Status}");
                    break;
            }
        }

        async Task MonitorAsync()
        {
            try
            {
                exitCode = await BluetoothConnectionMonitor.RunAsync(Publish, cancellation.Token);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Bluetooth monitoring failed: {error.Message}");
                exitCode = 1;
            }
            finally
            {
                panel.Post(Application.ExitThread);
            }
        }

        ConsoleCancelEventHandler cancel = (_, args) =>
        {
            args.Cancel = true;
            if (preview)
                panel.Post(Application.ExitThread);
            else
                cancellation.Cancel();
        };
        if (consoleControls)
            Console.CancelKeyPress += cancel;
        try
        {
            if (consoleControls)
                Console.WriteLine("Click the battery icon in the notification area. Press Ctrl+C to stop.");
            panel.Post(() =>
            {
                if (preview)
                {
                    panel.SetDevice("preview-keyboard", AccessoryKind.Keyboard, "Node 75 HP-1", 100, useSystemGlyph: false);
                    panel.SetDevice("preview-mouse", AccessoryKind.Mouse, "Wireless mouse", 93, useSystemGlyph: false);
                    panel.SetDevice("preview-headphones", AccessoryKind.Headphones, "Bluetooth headphones", 30, useSystemGlyph: false);
                    panel.SetDevice("preview-gamepad", AccessoryKind.Gamepad, "Wireless controller", 76, useSystemGlyph: false);
                }
                else
                {
                    _ = MonitorAsync();
                }
            });
            Application.Run();
            return exitCode;
        }
        finally
        {
            if (consoleControls)
                Console.CancelKeyPress -= cancel;
        }
    }

    public static int RunIconPreview()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var window = new TrayWindow();
        ConsoleCancelEventHandler cancel = (_, args) =>
        {
            args.Cancel = true;
            window.Post(Application.ExitThread);
        };
        Console.CancelKeyPress += cancel;
        try
        {
            Console.WriteLine("Showing numeric battery icon samples. Press Ctrl+C to stop.");
            window.Post(() =>
            {
                foreach (var percent in new byte[] { 0, 9, 30, 99, 100 })
                    window.SetBattery($"preview-{percent}", "Battery icon preview", percent);
            });
            Application.Run();
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }
}
