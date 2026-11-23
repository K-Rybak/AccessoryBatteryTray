using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using AccessoryBatteryTray.Features.Bluetooth.Common;

namespace AccessoryBatteryTray.Features.Bluetooth;

internal static class BluetoothAccessoryKind
{
    public static AccessoryKind Get(BluetoothLEDevice device)
    {
        try
        {
            var appearance = device.Appearance;
            if (appearance?.Category == BluetoothLEAppearanceCategories.HumanInterfaceDevice)
            {
                if (appearance.SubCategory == BluetoothLEAppearanceSubcategories.Keyboard)
                    return AccessoryKind.Keyboard;
                if (appearance.SubCategory == BluetoothLEAppearanceSubcategories.Mouse)
                    return AccessoryKind.Mouse;
                if (appearance.SubCategory == BluetoothLEAppearanceSubcategories.Gamepad)
                    return AccessoryKind.Gamepad;
            }
        }
        catch (COMException)
        {
            // Appearance is optional and does not affect the battery reading.
        }

        return AccessoryKind.Generic;
    }
}
