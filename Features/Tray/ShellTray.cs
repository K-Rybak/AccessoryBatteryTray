using System.Drawing;
using System.Runtime.InteropServices;

namespace AccessoryBatteryTray.Features.Tray;

internal static class ShellTray
{
    private const int TooltipCapacity = 128;
    private const uint MessageFlag = 0x01;
    private const uint IconFlag = 0x02;
    private const uint TooltipFlag = 0x04;
    private const uint ShowTooltipFlag = 0x80;
    private const uint NotifyIconVersion = 4;

    public static bool Show(nint window, uint id, nint icon, string deviceName, byte percent, bool update)
    {
        var suffix = $" · {percent}%";
        var nameLimit = TooltipCapacity - suffix.Length - 1;
        var name = string.IsNullOrWhiteSpace(deviceName) ? "Bluetooth accessory" : deviceName;
        if (name.Length > nameLimit)
        {
            var end = nameLimit - 1;
            if (char.IsHighSurrogate(name[end - 1]))
                end--;
            name = name[..end] + "…";
        }

        return Show(window, id, icon, name + suffix, update);
    }

    public static bool Show(nint window, uint id, nint icon, string tooltip, bool update, uint callbackMessage = 0)
    {
        var data = CreateData(window, id);
        data.Flags = IconFlag | TooltipFlag | ShowTooltipFlag;
        data.Icon = icon;
        data.Tooltip = tooltip;
        if (callbackMessage != 0)
        {
            data.Flags |= MessageFlag;
            data.CallbackMessage = callbackMessage;
        }

        if (update && ShellNotifyIcon(Command.Modify, ref data))
            return true;

        if (!ShellNotifyIcon(Command.Add, ref data))
            return false;

        data.TimeoutOrVersion = NotifyIconVersion;
        ShellNotifyIcon(Command.SetVersion, ref data);
        return true;
    }

    public static void Delete(nint window, uint id)
    {
        var data = CreateData(window, id);
        ShellNotifyIcon(Command.Delete, ref data);
    }

    public static void SetFocus(nint window, uint id)
    {
        var data = CreateData(window, id);
        ShellNotifyIcon(Command.SetFocus, ref data);
    }

    public static Rectangle? GetIconBounds(nint window, uint id)
    {
        var identifier = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            Window = window,
            Id = id
        };
        return ShellNotifyIconGetRect(ref identifier, out var bounds) == 0
            ? Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)
            : null;
    }

    private static NotifyIconData CreateData(nint window, uint id) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = window,
        Id = id,
        Tooltip = "",
        Info = "",
        InfoTitle = ""
    };

    private enum Command : uint
    {
        Add = 0,
        Modify = 1,
        Delete = 2,
        SetFocus = 3,
        SetVersion = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public Guid Guid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = TooltipCapacity)]
        public string Tooltip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(Command message, ref NotifyIconData data);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect", ExactSpelling = true)]
    private static extern int ShellNotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRectangle bounds);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint window);
}
