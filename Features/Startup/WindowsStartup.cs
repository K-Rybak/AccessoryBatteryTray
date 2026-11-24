using System.Reflection.PortableExecutable;
using System.Security;
using Microsoft.Win32;

namespace AccessoryBatteryTray.Features.Startup;

internal static class WindowsStartup
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "AccessoryBatteryTray";
    private const int MaxRunCommandLength = 260;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(EntryName) is string command && !string.IsNullOrWhiteSpace(command);
    }

    public static string Enable(string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetFileName(fullPath), $"{EntryName}.exe", StringComparison.OrdinalIgnoreCase) ||
            !IsDesktopExecutable(fullPath))
            throw new ArgumentException("Choose the published AccessoryBatteryTray.exe built with OutputType=WinExe.");

        var command = $"\"{fullPath}\"";
        if (command.Length > MaxRunCommandLength)
            throw new ArgumentException($"The startup command exceeds {MaxRunCommandLength} characters. Publish to a shorter path.");

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new IOException("Could not open the current user's Run registry key.");
        key.SetValue(EntryName, command, RegistryValueKind.String);
        return fullPath;
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(EntryName, throwOnMissingValue: false);
    }

    public static bool IsStartupError(Exception error) =>
        error is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or BadImageFormatException or NotSupportedException;

    private static bool IsDesktopExecutable(string path)
    {
        using var file = File.OpenRead(path);
        using var executable = new PEReader(file);
        return executable.PEHeaders.PEHeader?.Subsystem == Subsystem.WindowsGui;
    }
}
