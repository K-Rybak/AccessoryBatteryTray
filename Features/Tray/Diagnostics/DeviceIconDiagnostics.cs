using System.Drawing.Imaging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace AccessoryBatteryTray.Features.Tray.Diagnostics;

internal static class DeviceIconDiagnostics
{
    public static async Task<int> RunAsync()
    {
        string output;
        try
        {
            output = Directory.CreateTempSubdirectory("AccessoryBatteryTray-icons-").FullName;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not create an output directory: {error.Message}");
            return 1;
        }

        Console.WriteLine($"Output directory: {output}");
        var classic = await ExportTransportAsync("classic",
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected), output);
        var lowEnergy = await ExportTransportAsync("le",
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected), output);
        return classic && lowEnergy ? 0 : 1;
    }

    private static async Task<bool> ExportTransportAsync(string transport, string selector, string output)
    {
        DeviceInformationCollection devices;
        try
        {
            devices = await DeviceInformation.FindAllAsync(
                selector, DeviceContainerLookup.EndpointProperties, DeviceInformationKind.AssociationEndpoint);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[{transport}] Enumeration failed: {Describe(error)}");
            return false;
        }

        Console.WriteLine($"[{transport}] Connected devices: {devices.Count}");
        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            var prefix = Path.Combine(output, $"{transport}-{index + 1:D2}");
            Console.WriteLine($"  {device.Name}");
            await ExportImagesAsync(device, prefix);
            await ExportContainerAsync(device, prefix);
        }

        return true;
    }

    private static async Task ExportContainerAsync(DeviceInformation endpoint, string prefix)
    {
        var hasCandidate = false;
        foreach (var (property, id) in DeviceContainerLookup.GetCandidates(endpoint))
        {
            hasCandidate = true;
            try
            {
                var container = await DeviceContainerLookup.OpenAsync(id);
                if (container is null)
                {
                    Console.WriteLine($"    DeviceContainer ({property}): not found");
                    continue;
                }

                Console.WriteLine($"    DeviceContainer ({property}): {container.Name}");
                await ExportImagesAsync(container, $"{prefix}-container");
                return;
            }
            catch (Exception error)
            {
                Console.WriteLine($"    DeviceContainer ({property}): {Describe(error)}");
            }
        }

        if (!hasCandidate)
            Console.WriteLine("    DeviceContainer: endpoint provided no container ID");
    }

    private static async Task ExportImagesAsync(DeviceInformation device, string prefix)
    {
        await ExportImageAsync(device, $"{prefix}-glyph.png", "GetGlyphThumbnailAsync",
            item => item.GetGlyphThumbnailAsync());
        await ExportImageAsync(device, $"{prefix}-thumbnail.png", "GetThumbnailAsync",
            item => item.GetThumbnailAsync());
    }

    private static async Task ExportImageAsync(
        DeviceInformation device, string path, string method,
        Func<DeviceInformation, IAsyncOperation<DeviceThumbnail>> getThumbnail)
    {
        try
        {
            using var image = await DeviceGlyphLoader.ReadImageAsync(device, getThumbnail);
            if (image is null)
            {
                Console.WriteLine($"    {method}: no image");
                return;
            }

            image.Save(path, ImageFormat.Png);
            Console.WriteLine($"    {method}: {Path.GetFileName(path)} ({image.Width}×{image.Height})");
        }
        catch (Exception error)
        {
            Console.WriteLine($"    {method}: {Describe(error)}");
        }
    }

    private static string Describe(Exception error) =>
        $"{error.GetType().Name} (0x{error.HResult:X8}): {error.Message}";
}
