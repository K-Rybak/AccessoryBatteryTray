using AccessoryBatteryTray.Features.Bluetooth.Common;

namespace AccessoryBatteryTray.Features.Bluetooth;

internal abstract record BatteryReport
{
    public sealed record Reading(
        byte Percent, bool SupportsNotify, bool SupportsIndicate, bool FromNotification = false,
        AccessoryKind Kind = AccessoryKind.Generic)
        : BatteryReport;

    public sealed record Unavailable(string Reason) : BatteryReport;

    public sealed record SubscriptionActive(bool UsesIndication) : BatteryReport;

    public sealed record SubscriptionFailed(string Reason) : BatteryReport;

    public sealed record Polling(TimeSpan Interval) : BatteryReport;
}
