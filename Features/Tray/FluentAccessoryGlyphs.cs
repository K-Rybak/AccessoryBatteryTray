using System.Drawing;
using AccessoryBatteryTray.Features.Bluetooth.Common;

namespace AccessoryBatteryTray.Features.Tray;

internal static class FluentAccessoryGlyphs
{
    public static Bitmap? Load(AccessoryKind kind)
    {
        var resource = kind switch
        {
            AccessoryKind.Keyboard => "Fluent.Keyboard.png",
            AccessoryKind.Headphones => "Fluent.Headphones.png",
            AccessoryKind.Gamepad => "Fluent.Games.png",
            _ => null
        };
        if (resource is null)
            return null;

        using var stream = typeof(FluentAccessoryGlyphs).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
            return null;

        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
