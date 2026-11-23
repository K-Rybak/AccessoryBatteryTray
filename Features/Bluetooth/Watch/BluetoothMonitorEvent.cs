using AccessoryBatteryTray.Enums;
using Windows.Devices.Enumeration;

namespace AccessoryBatteryTray.Features.Bluetooth.Watch;

internal abstract record BluetoothMonitorEvent(BluetoothTransportType Transport)
{
    public sealed record Connected(BluetoothTransportType Transport, string DeviceId, string DeviceName)
        : BluetoothMonitorEvent(Transport);

    public sealed record Disconnected(BluetoothTransportType Transport, string DeviceId, string DeviceName)
        : BluetoothMonitorEvent(Transport);

    public sealed record BatteryReported(
        BluetoothTransportType Transport, string DeviceId, string DeviceName, BatteryReport Report)
        : BluetoothMonitorEvent(Transport);

    public sealed record InitialEnumerationCompleted(BluetoothTransportType Transport, int ConnectedCount)
        : BluetoothMonitorEvent(Transport);

    public sealed record WatcherStopped(BluetoothTransportType Transport, DeviceWatcherStatus Status)
        : BluetoothMonitorEvent(Transport);
}
