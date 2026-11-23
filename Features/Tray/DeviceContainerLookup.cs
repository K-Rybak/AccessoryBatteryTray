using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace AccessoryBatteryTray.Features.Tray;

internal static class DeviceContainerLookup
{
    public static IReadOnlyList<string> EndpointProperties { get; } =
        ["System.Devices.ContainerId", "System.Devices.Aep.ContainerId"];

    public static IEnumerable<(string Property, Guid Id)> GetCandidates(DeviceInformation endpoint)
    {
        var seen = new HashSet<Guid>();
        foreach (var property in EndpointProperties)
        {
            if (endpoint.Properties.TryGetValue(property, out var value) &&
                value is Guid id && id != Guid.Empty && seen.Add(id))
                yield return (property, id);
        }
    }

    public static IAsyncOperation<DeviceInformation> OpenAsync(Guid id, IEnumerable<string>? properties = null) =>
        DeviceInformation.CreateFromIdAsync(id.ToString("B"), properties ?? Array.Empty<string>(),
            DeviceInformationKind.DeviceContainer);
}
