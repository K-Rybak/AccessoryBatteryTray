using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace AccessoryBatteryTray.Features.Bluetooth;

internal static class GattBatterySource
{
    public static Task<IReadOnlyList<BatteryReport>> ReadBatteryAsync(string deviceId) =>
        WithBatteryServicesAsync(deviceId, (_, services) => ReadLevelsAsync(services));

    public static async Task WatchBatteryAsync(string deviceId, Action<BatteryReport> report, Task disconnected)
    {
        var result = await WithBatteryServicesAsync(
            deviceId,
            (device, services) => WatchLevelsAsync(device, services, report, disconnected),
            disconnected);
        foreach (var update in result)
        {
            if (disconnected.IsCompleted)
                return;

            report(update);
        }
    }

    private static async Task<IReadOnlyList<BatteryReport>> WithBatteryServicesAsync(
        string deviceId,
        Func<BluetoothLEDevice, IReadOnlyList<GattDeviceService>, Task<IReadOnlyList<BatteryReport>>> useServices,
        Task? disconnected = null)
    {
        if (disconnected?.IsCompleted == true)
            return [];

        var operation = "Open LE device";
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (device is null)
                return [new BatteryReport.Unavailable($"{operation}: Windows returned no device")];

            if (disconnected?.IsCompleted == true || device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                return [new BatteryReport.Unavailable("Skipped: LE device is no longer connected")];

            operation = "Discover Battery Service (0x180F)";
            var services = await device.GetGattServicesForUuidAsync(
                GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
            try
            {
                if (services.Status != GattCommunicationStatus.Success)
                    return [new BatteryReport.Unavailable($"{operation}: {FormatStatus(services.Status, services.ProtocolError)}")];

                if (services.Services.Count == 0)
                    return [new BatteryReport.Unavailable($"{operation}: service not found")];

                return await useServices(device, services.Services);
            }
            finally
            {
                foreach (var service in services.Services)
                    service.Dispose();
            }
        }
        catch (Exception error)
        {
            return [new BatteryReport.Unavailable($"{operation}: {Describe(error)}")];
        }
    }

    private static async Task<IReadOnlyList<BatteryReport>> ReadLevelsAsync(IReadOnlyList<GattDeviceService> services)
    {
        var results = new List<BatteryReport>();
        foreach (var service in services)
        {
            var (levels, error) = await FindLevelsAsync(service);
            if (error is not null)
                results.Add(new BatteryReport.Unavailable(error));

            foreach (var level in levels)
                results.Add(await ReadLevelAsync(level));
        }

        return results;
    }

    private static async Task<IReadOnlyList<BatteryReport>> WatchLevelsAsync(
        BluetoothLEDevice device, IReadOnlyList<GattDeviceService> services, Action<BatteryReport> report, Task disconnected)
    {
        var levels = new List<GattCharacteristic>();
        foreach (var service in services)
        {
            if (disconnected.IsCompleted)
                return [];

            var (serviceLevels, error) = await FindLevelsAsync(service);
            levels.AddRange(serviceLevels);
            if (error is not null)
                report(new BatteryReport.Unavailable(error));
        }

        await Task.WhenAll(levels.Select(level => WatchLevelAsync(device, level, report, disconnected)));
        return [];
    }

    private static async Task<(IReadOnlyList<GattCharacteristic> Levels, string? Error)> FindLevelsAsync(
        GattDeviceService service)
    {
        const string operation = "Discover Battery Level (0x2A19)";
        try
        {
            var result = await service.GetCharacteristicsForUuidAsync(
                GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
            if (result.Status != GattCommunicationStatus.Success)
                return ([], $"{operation}: {FormatStatus(result.Status, result.ProtocolError)}");

            return result.Characteristics.Count == 0
                ? ([], $"{operation}: characteristic not found")
                : (result.Characteristics, null);
        }
        catch (Exception error)
        {
            return ([], $"{operation}: {Describe(error)}");
        }
    }

    private static async Task<BatteryReport> ReadLevelAsync(GattCharacteristic characteristic)
    {
        try
        {
            return await ReadLevelCoreAsync(characteristic);
        }
        catch (Exception error)
        {
            return new BatteryReport.Unavailable($"Read Battery Level: {Describe(error)}");
        }
    }

    private static async Task<BatteryReport> ReadLevelCoreAsync(GattCharacteristic characteristic)
    {
        var properties = characteristic.CharacteristicProperties;
        if (!properties.HasFlag(GattCharacteristicProperties.Read))
            return new BatteryReport.Unavailable("Battery Level does not support reads");

        var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
        if (result.Status != GattCommunicationStatus.Success)
            return new BatteryReport.Unavailable($"Read Battery Level: {FormatStatus(result.Status, result.ProtocolError)}");

        if (result.Value is null || result.Value.Length != 1)
            return new BatteryReport.Unavailable($"Read Battery Level: expected 1 byte, got {result.Value?.Length ?? 0}");

        var level = ReadByte(result.Value);
        if (level > 100)
            return new BatteryReport.Unavailable($"Read Battery Level: value {level} is outside 0–100");

        return CreateReading(level, properties);
    }

    private static async Task WatchLevelAsync(
        BluetoothLEDevice device, GattCharacteristic characteristic, Action<BatteryReport> report, Task disconnected)
    {
        if (!IsConnected())
            return;

        report(await ReadLevelAsync(characteristic));
        if (!IsConnected() || await TryWatchNotificationsAsync(characteristic, report, disconnected))
            return;

        if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read) && IsConnected())
            await PollLevelAsync(characteristic, report, disconnected, IsConnected);
        return;

        bool IsConnected() => !disconnected.IsCompleted &&
                              device.ConnectionStatus == BluetoothConnectionStatus.Connected;
    }

    private static async Task<bool> TryWatchNotificationsAsync(
        GattCharacteristic characteristic, Action<BatteryReport> report, Task disconnected)
    {
        var properties = characteristic.CharacteristicProperties;
        var configuration = GetNotificationConfiguration(properties);
        if (configuration == GattClientCharacteristicConfigurationDescriptorValue.None)
            return false;

        void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (disconnected.IsCompleted || args.CharacteristicValue.Length != 1)
                return;

            var percent = ReadByte(args.CharacteristicValue);
            if (percent <= 100)
                report(CreateReading(percent, properties, fromNotification: true));
        }

        characteristic.ValueChanged += OnValueChanged;
        try
        {
            var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(configuration);
            if (status != GattCommunicationStatus.Success)
            {
                report(new BatteryReport.SubscriptionFailed(status.ToString()));
                return false;
            }

            report(new BatteryReport.SubscriptionActive(
                configuration == GattClientCharacteristicConfigurationDescriptorValue.Indicate));
            await disconnected;
            return true;
        }
        catch (Exception error)
        {
            report(new BatteryReport.SubscriptionFailed(Describe(error)));
            return false;
        }
        finally
        {
            characteristic.ValueChanged -= OnValueChanged;
        }
    }

    private static GattClientCharacteristicConfigurationDescriptorValue GetNotificationConfiguration(
        GattCharacteristicProperties properties)
    {
        if (properties.HasFlag(GattCharacteristicProperties.Notify))
            return GattClientCharacteristicConfigurationDescriptorValue.Notify;

        if (properties.HasFlag(GattCharacteristicProperties.Indicate))
            return GattClientCharacteristicConfigurationDescriptorValue.Indicate;

        return GattClientCharacteristicConfigurationDescriptorValue.None;
    }

    private static async Task PollLevelAsync(
        GattCharacteristic characteristic, Action<BatteryReport> report, Task disconnected, Func<bool> isConnected)
    {
        var interval = TimeSpan.FromMinutes(5);
        report(new BatteryReport.Polling(interval));
        while (isConnected())
        {
            try
            {
                await disconnected.WaitAsync(interval);
                return;
            }
            catch (TimeoutException)
            {
                if (isConnected())
                    report(await ReadLevelAsync(characteristic));
            }
        }
    }

    private static BatteryReport.Reading CreateReading(
        byte percent, GattCharacteristicProperties properties, bool fromNotification = false) =>
        new(percent,
            properties.HasFlag(GattCharacteristicProperties.Notify),
            properties.HasFlag(GattCharacteristicProperties.Indicate),
            fromNotification);

    private static byte ReadByte(IBuffer buffer)
    {
        using var reader = DataReader.FromBuffer(buffer);
        return reader.ReadByte();
    }

    private static string FormatStatus(GattCommunicationStatus status, byte? protocolError)
        => protocolError is { } code ? $"{status} (ATT error 0x{code:X2})" : status.ToString();

    private static string Describe(Exception error)
        => $"{error.GetType().Name} (0x{error.HResult:X8}): {error.Message}";
}
