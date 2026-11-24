using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using AccessoryBatteryTray.Enums;

namespace AccessoryBatteryTray.Features.Bluetooth.Watch;

internal static partial class BluetoothConnectionMonitor
{
    public static Task<int> RunAsync(Action<BluetoothMonitorEvent> publish, CancellationToken shutdown) =>
        BluetoothRadioMonitor.RunAsync(session => RunWatchersAsync(publish, session, shutdown), shutdown);

    private static async Task<int> RunWatchersAsync(
        Action<BluetoothMonitorEvent> publish, CancellationToken cancellationToken, CancellationToken shutdown)
    {
        var stopped = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var classic = new ConnectionWatcher(
            BluetoothTransportType.Classic,
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            stopped,
            publish);
        var lowEnergy = new ConnectionWatcher(
            BluetoothTransportType.Le,
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            stopped,
            publish,
            GattBatterySource.WatchBatteryAsync);

        await using var cancellation = cancellationToken.Register(() => stopped.TrySetResult(0));
        try
        {
            if (stopped.Task.IsCompleted)
                return await stopped.Task;

            classic.Start();
            lowEnergy.Start();
            return await stopped.Task;
        }
        finally
        {
            stopped.TrySetResult(0);
            var reportDisconnected = !shutdown.IsCancellationRequested;
            try
            {
                lowEnergy.Stop(reportDisconnected);
            }
            finally
            {
                classic.Stop(reportDisconnected);
            }
        }
    }

    private sealed class ConnectionWatcher
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, Connection> _connections = new(StringComparer.OrdinalIgnoreCase);
        private readonly DeviceWatcher _watcher;
        private readonly BluetoothTransportType _transport;
        private readonly TaskCompletionSource<int> _stopped;
        private readonly Action<BluetoothMonitorEvent> _publish;
        private readonly Func<string, Action<BatteryReport>, Task, Task>? _watchBattery;

        public ConnectionWatcher(
            BluetoothTransportType transport,
            string selector,
            TaskCompletionSource<int> stopped,
            Action<BluetoothMonitorEvent> publish,
            Func<string, Action<BatteryReport>, Task, Task>? watchBattery = null)
        {
            this._transport = transport;
            this._stopped = stopped;
            _publish = publish;
            this._watchBattery = watchBattery;
            _watcher = DeviceInformation.CreateWatcher(selector);
            _watcher.Added += OnAdded;
            _watcher.Updated += OnUpdated;
            _watcher.Removed += OnRemoved;
            _watcher.EnumerationCompleted += OnEnumerationCompleted;
            _watcher.Stopped += OnStopped;
        }

        public void Start() => _watcher.Start();

        public void Stop(bool reportDisconnected)
        {
            lock (_gate)
            {
                foreach (var connection in _connections.Values)
                {
                    connection.Disconnect();
                    if (reportDisconnected)
                    {
                        _publish(new BluetoothMonitorEvent.Disconnected(
                            _transport, connection.Device.Id, connection.Device.Name));
                    }
                }

                _connections.Clear();
            }

            _watcher.Added -= OnAdded;
            _watcher.Updated -= OnUpdated;
            _watcher.Removed -= OnRemoved;
            _watcher.EnumerationCompleted -= OnEnumerationCompleted;
            _watcher.Stopped -= OnStopped;
            if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                _watcher.Stop();
        }

        private void OnAdded(DeviceWatcher sender, DeviceInformation device)
        {
            var connection = new Connection(device);
            lock (_gate)
            {
                if (_stopped.Task.IsCompleted || !_connections.TryAdd(device.Id, connection))
                    return;

                _publish(new BluetoothMonitorEvent.Connected(_transport, device.Id, device.Name));
            }

            if (_watchBattery is not null)
                _ = _watchBattery(device.Id, result => ReportBattery(connection, result), connection.Disconnected);
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
                if (_stopped.Task.IsCompleted || !_connections.Remove(update.Id, out var connection)) return;
                connection.Disconnect();
                _publish(new BluetoothMonitorEvent.Disconnected(_transport, update.Id, connection.Device.Name));
            }
        }

        private void OnEnumerationCompleted(DeviceWatcher sender, object args)
        {
            lock (_gate)
            {
                if (!_stopped.Task.IsCompleted)
                    _publish(new BluetoothMonitorEvent.InitialEnumerationCompleted(_transport, _connections.Count));
            }
        }

        private void OnStopped(DeviceWatcher sender, object args)
        {
            lock (_gate)
            {
                if (_stopped.TrySetResult(1))
                    _publish(new BluetoothMonitorEvent.WatcherStopped(_transport, sender.Status));
            }
        }

        private void ReportBattery(Connection connection, BatteryReport result)
        {
            lock (_gate)
            {
                if (!_stopped.Task.IsCompleted &&
                    _connections.TryGetValue(connection.Device.Id, out var current) &&
                    ReferenceEquals(current, connection))
                {
                    _publish(new BluetoothMonitorEvent.BatteryReported(
                        _transport, connection.Device.Id, connection.Device.Name, result));
                }
            }
        }

        private sealed class Connection(DeviceInformation device)
        {
            private readonly TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public DeviceInformation Device { get; } = device;
            public Task Disconnected => _disconnected.Task;

            public void Disconnect() => _disconnected.TrySetResult();
        }
    }
}
