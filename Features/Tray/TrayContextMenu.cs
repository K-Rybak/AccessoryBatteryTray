using System.Drawing;
using System.Windows.Forms;
using AccessoryBatteryTray.Features.Startup;

namespace AccessoryBatteryTray.Features.Tray;

internal sealed class TrayContextMenu : IDisposable
{
    private readonly AccessoryPanel _panel;
    private readonly TrayDeviceList _devices;
    private readonly ContextMenuStrip _menu = new();
    private readonly List<ToolStripMenuItem> _deviceItems = [];
    private readonly ToolStripSeparator _deviceSeparator = new();
    private readonly ToolStripMenuItem _startup = new("Запускать с Windows");

    public TrayContextMenu(AccessoryPanel panel, TrayDeviceList devices, Action exit)
    {
        _panel = panel;
        _devices = devices;
        _startup.Click += (_, _) => ToggleStartup();
        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => exit();
        _menu.Items.AddRange(new ToolStripItem[] { _deviceSeparator, _startup, exitItem });
        _menu.Closed += (_, args) =>
        {
            if (args.CloseReason == ToolStripDropDownCloseReason.Keyboard)
                _panel.FocusTrayIcon();
        };
        _panel.ContextMenuRequested += Show;
        _panel.TrayIconHidden += Close;
    }

    private void Show(Point location)
    {
        RefreshDeviceItems();
        try
        {
            _startup.Checked = WindowsStartup.IsEnabled();
            _startup.Enabled = true;
        }
        catch (Exception error) when (WindowsStartup.IsStartupError(error))
        {
            _startup.Checked = false;
            _startup.Enabled = false;
        }

        _menu.Show(location);
    }

    private void RefreshDeviceItems()
    {
        foreach (var item in _deviceItems)
        {
            _menu.Items.Remove(item);
            item.Dispose();
        }
        _deviceItems.Clear();

        foreach (var device in _devices.Devices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = device.Name.Replace("&", "&&", StringComparison.Ordinal);
            var item = new ToolStripMenuItem($"{name} · {device.Percent}%")
            {
                Checked = _devices.IsVisible(device.Id)
            };
            item.Click += (_, _) => ToggleDeviceVisibility(device.Id);
            _menu.Items.Insert(_deviceItems.Count, item);
            _deviceItems.Add(item);
        }

        _deviceSeparator.Available = _deviceItems.Count > 0;
    }

    private void ToggleDeviceVisibility(string id)
    {
        try
        {
            _devices.ToggleVisibility(id);
        }
        catch (Exception error) when (TrayDevicePreferences.IsAccessError(error))
        {
            MessageBox.Show($"Не удалось сохранить видимость устройства.\n\n{error.Message}", "AccessoryBatteryTray",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Close() => _menu.Close();

    private void ToggleStartup()
    {
        try
        {
            if (WindowsStartup.IsEnabled())
                WindowsStartup.Disable();
            else
                WindowsStartup.Enable(Environment.ProcessPath ?? throw new InvalidOperationException(
                    "Открой опубликованный AccessoryBatteryTray.exe, чтобы включить автозапуск."));
        }
        catch (Exception error) when (WindowsStartup.IsStartupError(error) || error is InvalidOperationException)
        {
            MessageBox.Show($"Не удалось изменить автозапуск.\n\n{error.Message}", "AccessoryBatteryTray",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void Dispose()
    {
        _panel.TrayIconHidden -= Close;
        _panel.ContextMenuRequested -= Show;
        _menu.Dispose();
    }
}
