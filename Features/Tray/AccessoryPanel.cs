using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AccessoryBatteryTray.Features.Bluetooth;
using AccessoryBatteryTray.Features.Bluetooth.Common;

namespace AccessoryBatteryTray.Features.Tray;

internal sealed class AccessoryPanel : Form
{
    private const uint IconId = 1;
    private const int IconCallbackMessage = 0x8001;
    private const int ContextMenuNotification = 0x007b;
    private const int LeftButtonUp = 0x0202;
    private const int NotifySelect = 0x0400;
    private const int NotifyKeySelect = 0x0401;
    private const int PowerBroadcastMessage = 0x0218;
    private const int PowerSuspend = 0x0004;
    private const int PowerResumeAutomatic = 0x0012;
    private const uint NotifyWindowHandle = 0;
    private const int AnchorGap = 6;
    private const int MaxColumns = 3;
    private const int PanelPadding = 8;
    private const int TileWidth = 90;
    private const int TileHeight = 42;
    private readonly uint _taskbarCreated = ShellTray.RegisterWindowMessage("TaskbarCreated");
    private readonly ToolTip _toolTip = new();
    private readonly Dictionary<string, DeviceTile> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly FlowLayoutPanel _devices;
    private readonly bool _watchSystemPower;
    private nint _powerRegistration;
    private bool _iconVisible;

    public event Action? Suspending;
    public event Action? Resumed;
    public event Action<Point>? ContextMenuRequested;
    public event Action? TrayIconHidden;

    public AccessoryPanel(bool watchSystemPower)
    {
        _watchSystemPower = watchSystemPower;
        Text = "Accessory batteries";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(32, 35, 41);
        ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.WhiteSmoke;

        _devices = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true
        };
        Controls.Add(_devices);
        _ = Handle;
    }

    public void Post(Action action) => BeginInvoke(action);

    public void SetDevice(string id, AccessoryKind kind, string name, byte percent, bool useSystemGlyph)
    {
        var added = false;
        if (!_tiles.TryGetValue(id, out var tile))
        {
            tile = new DeviceTile(monochromeFallback: !useSystemGlyph) { Margin = Padding.Empty };
            _tiles.Add(id, tile);
            _devices.Controls.Add(tile);
            added = true;
        }

        var description = $"{name} · {percent}%";
        tile.UpdateDevice(kind, percent, description);
        _toolTip.SetToolTip(tile, description);
        if (!_iconVisible)
            _iconVisible = RegisterIcon();
        if (added && Visible)
            Reposition();
        if (added)
        {
            if (useSystemGlyph)
                _ = LoadDeviceGlyphAsync(id, kind, tile);
            else if (FluentAccessoryGlyphs.Load(kind) is { } previewGlyph)
                tile.SetGlyph(previewGlyph);
        }
    }

    private async Task LoadDeviceGlyphAsync(string id, AccessoryKind kind, DeviceTile tile)
    {
        var glyph = await DeviceGlyphLoader.LoadAsync(id, kind);
        if (glyph is null)
            return;

        if (!IsDisposed && _tiles.TryGetValue(id, out var current) && ReferenceEquals(current, tile))
            tile.SetGlyph(glyph);
        else
            glyph.Dispose();
    }

    public void RemoveDevice(string id)
    {
        if (!_tiles.Remove(id, out var tile))
            return;

        DisposeTile(tile);

        if (_tiles.Count == 0)
            HideTrayIcon();
        else if (Visible)
            Reposition();
    }

    public void ClearDevices()
    {
        HideTrayIcon();
        _devices.SuspendLayout();
        foreach (var tile in _tiles.Values)
            DisposeTile(tile);
        _tiles.Clear();
        _devices.ResumeLayout(performLayout: false);
    }

    private void DisposeTile(DeviceTile tile)
    {
        _toolTip.SetToolTip(tile, null);
        _devices.Controls.Remove(tile);
        tile.Dispose();
    }

    private void HideTrayIcon()
    {
        Hide();
        if (_iconVisible)
            ShellTray.Delete(Handle, IconId);
        _iconVisible = false;
        TrayIconHidden?.Invoke();
    }

    public void FocusTrayIcon()
    {
        if (_iconVisible)
            ShellTray.SetFocus(Handle, IconId);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_watchSystemPower)
            _powerRegistration = RegisterSuspendResumeNotification(Handle, NotifyWindowHandle);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_powerRegistration != 0)
        {
            UnregisterSuspendResumeNotification(_powerRegistration);
            _powerRegistration = 0;
        }

        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == IconCallbackMessage)
        {
            var notification = (int)(message.LParam.ToInt64() & 0xffff);
            if (notification is LeftButtonUp or NotifySelect or NotifyKeySelect)
                OpenPanel();
            else if (notification == ContextMenuNotification)
                OpenContextMenu();
            return;
        }

        if (_watchSystemPower && message.Msg == PowerBroadcastMessage)
        {
            switch (message.WParam.ToInt64())
            {
                case PowerSuspend:
                    Suspending?.Invoke();
                    message.Result = 1;
                    return;
                case PowerResumeAutomatic:
                    Resumed?.Invoke();
                    message.Result = 1;
                    return;
            }
        }

        base.WndProc(ref message);
        if (_taskbarCreated != 0 && message.Msg == _taskbarCreated)
        {
            Hide();
            TrayIconHidden?.Invoke();
            _iconVisible = _tiles.Count > 0 && RegisterIcon(update: _iconVisible);
        }
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_iconVisible && IsHandleCreated)
                ShellTray.Delete(Handle, IconId);
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OpenPanel()
    {
        if (_tiles.Count == 0)
            return;

        Reposition();
        if (!Visible)
        {
            Show();
            Reposition();
        }

        Activate();
    }

    private void OpenContextMenu()
    {
        if (!_iconVisible || ContextMenuRequested is not { } show)
            return;

        Hide();
        var location = ShellTray.GetIconBounds(Handle, IconId)?.Location ?? Cursor.Position;
        ShellTray.SetForegroundWindow(Handle);
        show(location);
    }

    private void Reposition()
    {
        var anchor = ShellTray.GetIconBounds(Handle, IconId) ?? new Rectangle(Cursor.Position, Size.Empty);
        var workArea = Screen.FromRectangle(anchor).WorkingArea;
        ResizeGrid(workArea);
        PositionPanel(anchor, workArea);
    }

    private void ResizeGrid(Rectangle workArea)
    {
        var padding = PanelPadding * DeviceDpi / 96;
        var tileWidth = TileWidth * DeviceDpi / 96;
        var tileHeight = TileHeight * DeviceDpi / 96;
        var columns = Math.Clamp((workArea.Width - 2 * padding) / tileWidth, 1, Math.Min(MaxColumns, _tiles.Count));
        var rows = (_tiles.Count + columns - 1) / columns;
        var maxRows = Math.Max(1, (workArea.Height - 2 * padding) / tileHeight);
        var scrollWidth = rows > maxRows ? SystemInformation.VerticalScrollBarWidth : 0;

        _devices.SuspendLayout();
        _devices.Padding = new Padding(padding);
        foreach (var tile in _tiles.Values)
            tile.Size = new Size(tileWidth, tileHeight);
        ClientSize = new Size(columns * tileWidth + 2 * padding + scrollWidth,
            Math.Min(rows, maxRows) * tileHeight + 2 * padding);
        _devices.ResumeLayout();
    }

    private void PositionPanel(Rectangle anchor, Rectangle workArea)
    {
        var gap = AnchorGap * DeviceDpi / 96;
        var above = anchor.Top - Height - gap;
        var below = anchor.Bottom + gap;
        var y = anchor.Bottom <= workArea.Top || (above < workArea.Top && below + Height <= workArea.Bottom)
            ? below
            : above;
        var x = anchor.Left + (anchor.Width - Width) / 2;
        Location = new Point(
            Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width)),
            Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height)));
    }

    private bool RegisterIcon(bool update = false)
    {
        var size = SystemInformation.SmallIconSize;
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(size.Width / 16f, size.Height / 16f);
        using var outline = new Pen(Color.FromArgb(28, 44, 63), 1.5f);
        using var terminal = new SolidBrush(outline.Color);
        graphics.FillRectangle(Brushes.WhiteSmoke, 1, 2.75f, 12, 10.5f);
        graphics.DrawRectangle(outline, 1, 2.75f, 12, 10.5f);
        graphics.FillRectangle(terminal, 13, 5.5f, 2.5f, 5);
        graphics.FillRectangle(Brushes.SeaGreen, 2.75f, 4.5f, 7.5f, 7);

        var icon = bitmap.GetHicon();
        try
        {
            return ShellTray.Show(Handle, IconId, icon, "Accessory batteries",
                update, callbackMessage: IconCallbackMessage);
        }
        finally
        {
            ShellTray.DestroyIcon(icon);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint RegisterSuspendResumeNotification(nint recipient, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterSuspendResumeNotification(nint registration);
}
