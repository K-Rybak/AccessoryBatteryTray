using System.Drawing;
using System.Windows.Forms;
using AccessoryBatteryTray.Features.Startup;

namespace AccessoryBatteryTray.Features.Tray;

internal sealed class TrayContextMenu : IDisposable
{
    private readonly AccessoryPanel _panel;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _startup = new("Запускать с Windows");

    public TrayContextMenu(AccessoryPanel panel, Action exit)
    {
        _panel = panel;
        _startup.Click += (_, _) => ToggleStartup();
        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => exit();
        _menu.Items.AddRange(new ToolStripItem[] { _startup, exitItem });
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
