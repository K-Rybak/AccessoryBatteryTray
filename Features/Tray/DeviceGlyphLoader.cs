using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;
using AccessoryBatteryTray.Features.Bluetooth.Common;

namespace AccessoryBatteryTray.Features.Tray;

internal static class DeviceGlyphLoader
{
    private const string CategoryIds = "System.Devices.CategoryIds";

    public static async Task<Bitmap?> LoadAsync(string deviceId, AccessoryKind kind)
    {
        if (FluentAccessoryGlyphs.Load(kind) is { } fluentGlyph)
            return fluentGlyph;

        var endpoint = await TryOpenEndpointAsync(deviceId, DeviceContainerLookup.EndpointProperties)
                       ?? await TryOpenEndpointAsync(deviceId, Array.Empty<string>());
        if (endpoint is null)
            return null;

        foreach (var (_, id) in DeviceContainerLookup.GetCandidates(endpoint))
        {
            try
            {
                var container = await DeviceContainerLookup.OpenAsync(
                    id, kind == AccessoryKind.Generic ? [CategoryIds] : null);
                if (container is null)
                    continue;

                if (kind == AccessoryKind.Generic && FluentAccessoryGlyphs.Load(GetContainerKind(container)) is { } categoryGlyph)
                    return categoryGlyph;

                if (await ReadImageAsync(container, device => device.GetGlyphThumbnailAsync()) is { } glyph)
                    return glyph;
            }
            catch (Exception error) when (IsImageUnavailable(error))
            {
                // Continue with the next container or the Bluetooth endpoint.
            }
        }

        try
        {
            return await ReadImageAsync(endpoint, device => device.GetGlyphThumbnailAsync());
        }
        catch (Exception error) when (IsImageUnavailable(error))
        {
            // Accessories with unavailable system glyphs keep their current silhouette.
            return null;
        }
    }

    private static AccessoryKind GetContainerKind(DeviceInformation container)
    {
        if (container.Properties.TryGetValue(CategoryIds, out var value) && value is string[] categories)
        {
            foreach (var category in categories)
            {
                var kind = category switch
                {
                    "Input.Keyboard" => AccessoryKind.Keyboard,
                    "Input.Mouse" => AccessoryKind.Mouse,
                    "Input.Gaming" => AccessoryKind.Gamepad,
                    "Audio.Headphone" or "Audio.Headset" => AccessoryKind.Headphones,
                    _ => AccessoryKind.Generic
                };
                if (kind != AccessoryKind.Generic)
                    return kind;
            }
        }

        return AccessoryKind.Generic;
    }

    private static async Task<DeviceInformation?> TryOpenEndpointAsync(string deviceId, IEnumerable<string> properties)
    {
        try
        {
            return await DeviceInformation.CreateFromIdAsync(deviceId, properties, DeviceInformationKind.AssociationEndpoint);
        }
        catch (Exception error) when (IsImageUnavailable(error))
        {
            return null;
        }
    }

    private static bool IsImageUnavailable(Exception error) =>
        error is ExternalException or UnauthorizedAccessException or ArgumentException or IOException
            or InvalidOperationException or NotSupportedException;

    public static async Task<Bitmap?> ReadImageAsync(
        DeviceInformation device, Func<DeviceInformation, IAsyncOperation<DeviceThumbnail>> getThumbnail)
    {
        using var thumbnail = await getThumbnail(device);
        if (thumbnail is null || thumbnail.Size == 0 || thumbnail.Size > int.MaxValue)
            return null;

        using var stream = thumbnail.GetInputStreamAt(0);
        using var reader = new DataReader(stream);
        var bytes = new byte[checked((int)thumbnail.Size)];
        if (await reader.LoadAsync((uint)bytes.Length) != (uint)bytes.Length)
            return null;

        reader.ReadBytes(bytes);
        return Decode(bytes);
    }

    private static Bitmap Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        if (bytes is [0, 0, 1, 0, ..])
        {
            using var icon = new Icon(stream);
            return icon.ToBitmap();
        }

        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
