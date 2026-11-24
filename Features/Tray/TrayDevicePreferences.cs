using System.Security;
using Microsoft.Win32;

namespace AccessoryBatteryTray.Features.Tray;

internal static class TrayDevicePreferences
{
    private const string HiddenDevicesKey = @"Software\AccessoryBatteryTray\HiddenDevices";

    public static HashSet<string> LoadHiddenIds()
    {
        using var key = Registry.CurrentUser.OpenSubKey(HiddenDevicesKey);
        return new HashSet<string>(key?.GetValueNames() ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    public static void SetHidden(string id, bool hidden)
    {
        if (hidden)
        {
            using var key = Registry.CurrentUser.CreateSubKey(HiddenDevicesKey)
                ?? throw new IOException("Could not open the hidden devices registry key.");
            key.SetValue(id, 1, RegistryValueKind.DWord);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(HiddenDevicesKey, writable: true);
            key?.DeleteValue(id, throwOnMissingValue: false);
        }
    }

    public static bool IsAccessError(Exception error) =>
        error is IOException or UnauthorizedAccessException or SecurityException;
}
