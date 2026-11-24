using AccessoryBatteryTray.Features.Bluetooth.Common;

namespace AccessoryBatteryTray.Features.Tray;

internal sealed class TrayDeviceList
{
    private readonly AccessoryPanel _panel;
    private readonly Dictionary<string, Device> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hidden;

    public TrayDeviceList(AccessoryPanel panel)
    {
        _panel = panel;
        try
        {
            _hidden = TrayDevicePreferences.LoadHiddenIds();
        }
        catch (Exception error) when (TrayDevicePreferences.IsAccessError(error))
        {
            _hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Console.Error.WriteLine($"Could not read device visibility preferences: {error.Message}");
        }
    }

    public IReadOnlyCollection<Device> Devices => _devices.Values;

    public bool IsVisible(string id) => !_hidden.Contains(id);

    public void UpdateDevice(string id, AccessoryKind kind, string name, byte percent)
    {
        var device = new Device(id, kind, name, percent);
        _devices[id] = device;
        if (IsVisible(id))
            ShowDevice(device);
        _panel.SetIconPresence(hasDevices: true);
    }

    public void RemoveDevice(string id)
    {
        if (!_devices.Remove(id))
            return;

        _panel.RemoveDevice(id);
        _panel.SetIconPresence(hasDevices: _devices.Count > 0);
    }

    public void Clear()
    {
        _devices.Clear();
        _panel.ClearDevices();
    }

    public void ToggleVisibility(string id)
    {
        if (!_devices.TryGetValue(id, out var device))
            return;

        var hide = IsVisible(id);
        TrayDevicePreferences.SetHidden(id, hide);
        if (hide)
        {
            _hidden.Add(id);
            _panel.RemoveDevice(id);
        }
        else
        {
            _hidden.Remove(id);
            ShowDevice(device);
        }
    }

    private void ShowDevice(Device device) =>
        _panel.SetDevice(device.Id, device.Kind, device.Name, device.Percent, useSystemGlyph: true);

    public sealed record Device(string Id, AccessoryKind Kind, string Name, byte Percent);
}
