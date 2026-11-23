using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace AccessoryBatteryTray.Features.Bluetooth;

internal static class GattBatterySource
{
    public static Task<string> ReadBatteryAsync(string deviceId) =>
        WithBatteryServicesAsync(deviceId, (_, services) => ReadLevelsAsync(services));

    public static async Task WatchBatteryAsync(string deviceId, Action<string> report, Task disconnected)
    {
        var result = await WithBatteryServicesAsync(
            deviceId,
            (device, services) => WatchLevelsAsync(device, services, report, disconnected),
            disconnected);
        if (result.Length > 0 && !disconnected.IsCompleted)
            report(result);
    }

    private static async Task<string> WithBatteryServicesAsync(
        string deviceId,
        Func<BluetoothLEDevice, IReadOnlyList<GattDeviceService>, Task<string>> useServices,
        Task? disconnected = null)
    {
        if (disconnected?.IsCompleted == true)
            return "";

        var operation = "Open LE device";
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (device is null)
                return $"{operation}: Windows returned no device";

            if (disconnected?.IsCompleted == true || device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                return "Skipped: LE device is no longer connected";

            operation = "Discover Battery Service (0x180F)";
            var services = await device.GetGattServicesForUuidAsync(
                GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
            try
            {
                if (services.Status != GattCommunicationStatus.Success)
                    return $"{operation}: {FormatStatus(services.Status, services.ProtocolError)}";

                if (services.Services.Count == 0)
                    return $"{operation}: service not found";

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
            return $"{operation}: {Describe(error)}";
        }
    }

    private static async Task<string> ReadLevelsAsync(IReadOnlyList<GattDeviceService> services)
    {
        var results = new List<string>();
        foreach (var service in services)
        {
            var (levels, error) = await FindLevelsAsync(service);
            if (error is not null)
                results.Add(error);

            foreach (var level in levels)
                results.Add(await ReadLevelAsync(level));
        }

        return string.Join(" | ", results);
    }

    private static async Task<string> WatchLevelsAsync(
        BluetoothLEDevice device, IReadOnlyList<GattDeviceService> services, Action<string> report, Task disconnected)
    {
        var levels = new List<GattCharacteristic>();
        var errors = new List<string>();
        foreach (var service in services)
        {
            if (disconnected.IsCompleted)
                return "";

            var (serviceLevels, error) = await FindLevelsAsync(service);
            levels.AddRange(serviceLevels);
            if (error is not null)
                errors.Add(error);
        }

        if (levels.Count == 0)
            return string.Join(" | ", errors);

        foreach (var error in errors)
            report(error);

        await Task.WhenAll(levels.Select(level => WatchLevelAsync(device, level, report, disconnected)));
        return "";
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

    private static async Task<string> ReadLevelAsync(GattCharacteristic characteristic)
    {
        try
        {
            return await ReadLevelCoreAsync(characteristic);
        }
        catch (Exception error)
        {
            return $"Read Battery Level: {Describe(error)}";
        }
    }

    private static async Task<string> ReadLevelCoreAsync(GattCharacteristic characteristic)
    {
        var properties = characteristic.CharacteristicProperties;
        if (!properties.HasFlag(GattCharacteristicProperties.Read))
            return "Battery Level does not support reads";

        var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
        if (result.Status != GattCommunicationStatus.Success)
            return $"Read Battery Level: {FormatStatus(result.Status, result.ProtocolError)}";

        if (result.Value is null || result.Value.Length != 1)
            return $"Read Battery Level: expected 1 byte, got {result.Value?.Length ?? 0}";

        var level = ReadByte(result.Value);
        if (level > 100)
            return $"Read Battery Level: value {level} is outside 0–100";

        var notify = properties.HasFlag(GattCharacteristicProperties.Notify);
        var indicate = properties.HasFlag(GattCharacteristicProperties.Indicate);
        return $"Battery: {level}% (GATT, Notify={notify}, Indicate={indicate})";
    }

    private static async Task WatchLevelAsync(
        BluetoothLEDevice device, GattCharacteristic characteristic, Action<string> report, Task disconnected)
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
        GattCharacteristic characteristic, Action<string> report, Task disconnected)
    {
        var configuration = GetNotificationConfiguration(characteristic.CharacteristicProperties);
        if (configuration == GattClientCharacteristicConfigurationDescriptorValue.None)
            return false;

        void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (disconnected.IsCompleted || args.CharacteristicValue.Length != 1)
                return;

            var percent = ReadByte(args.CharacteristicValue);
            if (percent <= 100)
                report($"Battery update: {percent}% (GATT notification)");
        }

        characteristic.ValueChanged += OnValueChanged;
        try
        {
            var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(configuration);
            if (status != GattCommunicationStatus.Success)
            {
                report($"GATT subscription failed: {status}");
                return false;
            }

            report($"GATT subscription active ({configuration})");
            await disconnected;
            return true;
        }
        catch (Exception error)
        {
            report($"GATT subscription failed: {Describe(error)}");
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
        GattCharacteristic characteristic, Action<string> report, Task disconnected, Func<bool> isConnected)
    {
        var interval = TimeSpan.FromMinutes(5);
        report($"Polling Battery Level every {interval.TotalMinutes:0} minutes");
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
