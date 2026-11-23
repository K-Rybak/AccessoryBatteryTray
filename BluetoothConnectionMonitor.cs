using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace AccessoryBatteryTray;

internal static class BluetoothConnectionMonitor
{
    public static async Task<int> RunAsync()
    {
        var stopped = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var classic = new ConnectionWatcher(
            "Classic",
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            stopped);
        var lowEnergy = new ConnectionWatcher(
            "LE",
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            stopped,
            BluetoothDiagnostics.ReadBatteryAsync);

        ConsoleCancelEventHandler cancel = (_, args) =>
        {
            args.Cancel = true;
            stopped.TrySetResult(0);
        };
        Console.CancelKeyPress += cancel;
        try
        {
            Console.WriteLine("Watching Bluetooth connections. Press Ctrl+C to stop.");
            classic.Start();
            lowEnergy.Start();
            return await stopped.Task;
        }
        catch (Exception error)
        {
            await Console.Error.WriteLineAsync($"Could not start Bluetooth monitoring (0x{error.HResult:X8}): {error.Message}");
            return 1;
        }
        finally
        {
            stopped.TrySetResult(0);
            lowEnergy.Stop();
            classic.Stop();
            Console.CancelKeyPress -= cancel;
        }
    }

    private sealed class ConnectionWatcher
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, Connection> _connections = new(StringComparer.OrdinalIgnoreCase);
        private readonly DeviceWatcher _watcher;
        private readonly string _transport;
        private readonly TaskCompletionSource<int> _stopped;
        private readonly Func<string, Task<string>>? _readBattery;

        public ConnectionWatcher(
            string transport,
            string selector,
            TaskCompletionSource<int> stopped,
            Func<string, Task<string>>? readBattery = null)
        {
            this._transport = transport;
            this._stopped = stopped;
            this._readBattery = readBattery;
            _watcher = DeviceInformation.CreateWatcher(selector);
            _watcher.Added += OnAdded;
            _watcher.Updated += OnUpdated;
            _watcher.Removed += OnRemoved;
            _watcher.EnumerationCompleted += OnEnumerationCompleted;
            _watcher.Stopped += OnStopped;
        }

        public void Start() => _watcher.Start();

        public void Stop()
        {
            _watcher.Added -= OnAdded;
            _watcher.Updated -= OnUpdated;
            _watcher.Removed -= OnRemoved;
            _watcher.EnumerationCompleted -= OnEnumerationCompleted;
            _watcher.Stopped -= OnStopped;
            if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                _watcher.Stop();

            lock (_gate)
                _connections.Clear();
        }

        private void OnAdded(DeviceWatcher sender, DeviceInformation device)
        {
            var connection = new Connection(device);
            lock (_gate)
            {
                if (_stopped.Task.IsCompleted || !_connections.TryAdd(device.Id, connection))
                    return;

                Console.WriteLine($"[{_transport}] Connected: {device.Name}");
            }

            if (_readBattery is not null)
                _ = ReportBatteryAsync(connection, _readBattery);
        }

        private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            lock (_gate)
            {
                if (!_stopped.Task.IsCompleted && _connections.TryGetValue(update.Id, out var connection))
                    connection.Device.Update(update);
            }
        }

        private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            lock (_gate)
            {
                if (!_stopped.Task.IsCompleted && _connections.Remove(update.Id, out var connection))
                    Console.WriteLine($"[{_transport}] Disconnected: {connection.Device.Name}");
            }
        }

        private void OnEnumerationCompleted(DeviceWatcher sender, object args)
        {
            lock (_gate)
            {
                if (!_stopped.Task.IsCompleted)
                    Console.WriteLine($"[{_transport}] Initial enumeration complete: {_connections.Count} connected");
            }
        }

        private void OnStopped(DeviceWatcher sender, object args)
        {
            lock (_gate)
            {
                if (_stopped.TrySetResult(1))
                    Console.Error.WriteLine($"[{_transport}] Watcher stopped unexpectedly: {sender.Status}");
            }
        }

        private async Task ReportBatteryAsync(Connection connection, Func<string, Task<string>> read)
        {
            var result = await read(connection.Device.Id);
            lock (_gate)
            {
                if (!_stopped.Task.IsCompleted &&
                    _connections.TryGetValue(connection.Device.Id, out var current) &&
                    ReferenceEquals(current, connection))
                {
                    Console.WriteLine($"[{_transport}] {connection.Device.Name} | {result}");
                }
            }
        }

        private sealed class Connection(DeviceInformation device)
        {
            public DeviceInformation Device { get; } = device;
        }
    }
}
