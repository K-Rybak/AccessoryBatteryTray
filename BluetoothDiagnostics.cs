using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace AccessoryBatteryTray;

internal static class BluetoothDiagnostics
{
    private const string IsConnected = "System.Devices.Aep.IsConnected";
    private static readonly string[] ConnectionProperties = [IsConnected];

    public static async Task<bool> RunAsync(
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
            Console.WriteLine($"Enumeration failed: {Describe(error)}");
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

    public static async Task<string> ReadBatteryAsync(string deviceId)
    {
        var operation = "Open LE device";
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (device is null)
                return $"{operation}: Windows returned no device";

            if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
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

                var results = new List<string>();
                foreach (var service in services.Services)
                {
                    operation = "Discover Battery Level (0x2A19)";
                    var characteristics = await service.GetCharacteristicsForUuidAsync(
                        GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
                    if (characteristics.Status != GattCommunicationStatus.Success)
                    {
                        results.Add($"{operation}: {FormatStatus(characteristics.Status, characteristics.ProtocolError)}");
                        continue;
                    }

                    if (characteristics.Characteristics.Count == 0)
                        results.Add($"{operation}: characteristic not found");

                    foreach (var characteristic in characteristics.Characteristics)
                    {
                        operation = "Read Battery Level";
                        results.Add(await ReadLevelAsync(characteristic));
                    }
                }

                return string.Join(" | ", results);
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

    private static async Task<string> ReadLevelAsync(GattCharacteristic characteristic)
    {
        var properties = characteristic.CharacteristicProperties;
        if (!properties.HasFlag(GattCharacteristicProperties.Read))
            return "Battery Level does not support reads";

        var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
        if (result.Status != GattCommunicationStatus.Success)
            return $"Read Battery Level: {FormatStatus(result.Status, result.ProtocolError)}";

        if (result.Value is null || result.Value.Length != 1)
            return $"Read Battery Level: expected 1 byte, got {result.Value?.Length ?? 0}";

        using var reader = DataReader.FromBuffer(result.Value);
        var level = reader.ReadByte();
        if (level > 100)
            return $"Read Battery Level: value {level} is outside 0–100";

        var notify = properties.HasFlag(GattCharacteristicProperties.Notify);
        var indicate = properties.HasFlag(GattCharacteristicProperties.Indicate);
        return $"Battery: {level}% (GATT, Notify={notify}, Indicate={indicate})";
    }

    private static string FormatStatus(GattCommunicationStatus status, byte? protocolError)
        => protocolError is { } code ? $"{status} (ATT error 0x{code:X2})" : status.ToString();

    private static string Describe(Exception error)
        => $"{error.GetType().Name} (0x{error.HResult:X8}): {error.Message}";
}