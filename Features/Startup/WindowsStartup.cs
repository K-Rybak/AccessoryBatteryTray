using System.Reflection.PortableExecutable;
using System.Security;
using Microsoft.Win32;

namespace AccessoryBatteryTray.Features.Startup;

internal static class WindowsStartup
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "AccessoryBatteryTray";
    private const int MaxRunCommandLength = 260;

    public static int Enable(string executablePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            if (!string.Equals(Path.GetFileName(fullPath), $"{EntryName}.exe", StringComparison.OrdinalIgnoreCase) ||
                !IsDesktopExecutable(fullPath))
            {
                Console.Error.WriteLine("Choose the published AccessoryBatteryTray.exe built with OutputType=WinExe.");
                return 1;
            }

            var command = $"\"{fullPath}\"";
            if (command.Length > MaxRunCommandLength)
            {
                Console.Error.WriteLine($"The startup command exceeds {MaxRunCommandLength} characters. Publish to a shorter path.");
                return 1;
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(EntryName, command, RegistryValueKind.String);
            Console.WriteLine($"Startup enabled for the current user: {fullPath}");
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException
                                      or ArgumentException or BadImageFormatException or NotSupportedException)
        {
            Console.Error.WriteLine($"Could not enable startup: {error.Message}");
            return 1;
        }
    }

    public static int Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(EntryName, throwOnMissingValue: false);
            Console.WriteLine("Startup disabled for the current user.");
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            Console.Error.WriteLine($"Could not disable startup: {error.Message}");
            return 1;
        }
    }

    private static bool IsDesktopExecutable(string path)
    {
        using var file = File.OpenRead(path);
        using var executable = new PEReader(file);
        return executable.PEHeaders.PEHeader?.Subsystem == Subsystem.WindowsGui;
    }
}
