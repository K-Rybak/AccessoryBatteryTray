namespace AccessoryBatteryTray.Features.Bluetooth;

internal static class BatteryConsoleText
{
    public static string Format(BatteryReport report) => report switch
    {
        BatteryReport.Reading { FromNotification: true } update =>
            $"Battery update: {update.Percent}% (GATT notification)",
        BatteryReport.Reading reading =>
            $"Battery: {reading.Percent}% (GATT, Notify={reading.SupportsNotify}, Indicate={reading.SupportsIndicate})",
        BatteryReport.Unavailable unavailable => unavailable.Reason,
        BatteryReport.SubscriptionActive active =>
            $"GATT subscription active ({(active.UsesIndication ? "Indicate" : "Notify")})",
        BatteryReport.SubscriptionFailed failed => $"GATT subscription failed: {failed.Reason}",
        BatteryReport.Polling polling =>
            $"Polling Battery Level every {polling.Interval.TotalMinutes:0} minutes",
        _ => throw new ArgumentOutOfRangeException(nameof(report))
    };
}
