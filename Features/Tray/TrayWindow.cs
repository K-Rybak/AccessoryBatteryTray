using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace AccessoryBatteryTray.Features.Tray;

internal sealed class TrayWindow : Form
{
    private readonly Dictionary<string, BatteryIcon> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint _taskbarCreated = ShellTray.RegisterWindowMessage("TaskbarCreated");
    private uint _nextIconId;

    public TrayWindow()
    {
        ShowInTaskbar = false;
        _ = Handle;
    }

    public void Post(Action action) => BeginInvoke(action);

    public void SetBattery(string deviceId, string deviceName, byte percent)
    {
        if (_icons.TryGetValue(deviceId, out var current) &&
            current.IsVisible && current.Percent == percent && current.DeviceName == deviceName)
        {
            return;
        }

        var icon = new BatteryIcon(current?.Id ?? ++_nextIconId, deviceName, percent);
        _icons[deviceId] = icon with { IsVisible = RenderIcon(icon, current?.IsVisible == true) };
    }

    public void RemoveBattery(string deviceId)
    {
        if (_icons.Remove(deviceId, out var icon))
            ShellTray.Delete(Handle, icon.Id);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (_taskbarCreated != 0 && message.Msg == _taskbarCreated)
        {
            foreach (var (deviceId, icon) in _icons.ToArray())
                _icons[deviceId] = icon with { IsVisible = RenderIcon(icon, update: false) };
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && IsHandleCreated)
        {
            foreach (var icon in _icons.Values)
                ShellTray.Delete(Handle, icon.Id);

            _icons.Clear();
        }

        base.Dispose(disposing);
    }

    private bool RenderIcon(BatteryIcon icon, bool update)
    {
        var imageSize = SystemInformation.SmallIconSize;
        using var bitmap = new Bitmap(imageSize.Width, imageSize.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var background = new SolidBrush(Color.FromArgb(28, 44, 63));
        var bounds = new RectangleF(1, 1, bitmap.Width - 2, bitmap.Height - 2);
        graphics.FillRectangle(background, bounds);

        var text = icon.Percent.ToString(CultureInfo.InvariantCulture);
        var fontSize = bounds.Height * (text.Length == 3 ? 0.58f : 0.86f);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = StringFormat.GenericTypographic;
        format.Alignment = StringAlignment.Center;
        format.LineAlignment = StringAlignment.Center;
        format.FormatFlags |= StringFormatFlags.NoWrap;
        graphics.DrawString(text, font, Brushes.White, bounds, format);

        var handle = bitmap.GetHicon();
        try
        {
            return ShellTray.Show(Handle, icon.Id, handle, icon.DeviceName, icon.Percent, update);
        }
        finally
        {
            ShellTray.DestroyIcon(handle);
        }
    }

    private sealed record BatteryIcon(uint Id, string DeviceName, byte Percent, bool IsVisible = false);
}
