namespace AccessoryBatteryTray.Features.Startup;

internal static class StartupConsole
{
    public static int Enable(string executablePath)
    {
        try
        {
            var fullPath = WindowsStartup.Enable(executablePath);
            Console.WriteLine($"Startup enabled for the current user: {fullPath}");
            return 0;
        }
        catch (Exception error) when (WindowsStartup.IsStartupError(error))
        {
            Console.Error.WriteLine($"Could not enable startup: {error.Message}");
            return 1;
        }
    }

    public static int Disable()
    {
        try
        {
            WindowsStartup.Disable();
            Console.WriteLine("Startup disabled for the current user.");
            return 0;
        }
        catch (Exception error) when (WindowsStartup.IsStartupError(error))
        {
            Console.Error.WriteLine($"Could not disable startup: {error.Message}");
            return 1;
        }
    }
}
