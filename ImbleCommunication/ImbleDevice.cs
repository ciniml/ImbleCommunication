using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using ImbleCommunication.Annotations;
using Reactive.Bindings.Extensions;

namespace ImbleCommunication
{
    /// <summary>
    /// Status of IMBLE device.
    /// </summary>
    public enum ImbleDeviceStatus
    {
        Initializing,
        Running,
    }

    /// <summary>
    /// EventArgs for DataArrived event of ImbleDevice class.
    /// </summary>
    public sealed class DataArrivedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the byte array which contains the received data.
        /// </summary>
        public byte[] Data { get; }
        /// <summary>
        /// Get the timestamp of the received data.
        /// </summary>
        public DateTimeOffset Timestamp { get; }

        public DataArrivedEventArgs(byte[] data, DateTimeOffset timestamp)
        {
            this.Data = data;
            this.Timestamp = timestamp;
        }
    }


    public class ImbleDevice : INotifyPropertyChanged, IDisposable
    {
        public static readonly Guid ServiceUuid = new Guid("ada99a7f-888b-4e9f-8080-07ddc240f3ce");
        public static readonly Guid ReadCharacteristicUuid = new Guid("ada99a7f-888b-4e9f-8081-07ddc240f3ce");
        public static readonly Guid WriteCharacteristicUuid = new Guid("ada99a7f-888b-4e9f-8082-07ddc240f3ce");

        public static readonly int MaxLengthOfData = 16;

        private const string ContainerIdProperty = "System.Devices.ContainerId";

        public static async Task<DeviceInformation[]> FindAllAsync(CancellationToken cancellationToken)
        {
            var selector = GattDeviceService.GetDeviceSelectorFromUuid(ServiceUuid);
            var collection = await DeviceInformation.FindAllAsync(selector, new[] {ContainerIdProperty}).AsTask(cancellationToken);
            var tasks = collection
                .Select(service => (string) service.Properties[ContainerIdProperty])
                .Distinct()
                .Select(deviceId => Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(deviceId).AsTask(cancellationToken))
                .ToArray();
            await Task.WhenAll(tasks);
            return tasks.Select(task => task.Result).ToArray();
        }

        /// <summary>
        /// Connect to an unconnected IMBLE device.
        /// </summary>
        /// <param name="device">An UnconnectedImbleDevice instance corresponding to the IMBLE device to connect to.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<ImbleDevice> ConnectAsync(UnconnectedImbleDevice device, CancellationToken cancellationToken)
        {
            var imble = new ImbleDevice();

            // I tried to use DeviceInformation.FindAllAsync combined with BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress
            // to obtain a device information corresponding to a Bluetooth address.
            // But this implementation is not feasible because DeviceInformation.FindAllAsync takes 10 to 20 seconds.
            // Thus, I've decided to use BluetoothLEDevice.FromBluetoothAddressAsync instead of DeviceInformation.FindAllAsync.
            var bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(device.Address);
            var deviceInformation = bleDevice.DeviceInformation;
            bleDevice.Dispose();    // Do not forget to dispose BLE related objects.

            await imble.Initialize(deviceInformation, device.Address, cancellationToken);
            return imble;
        }

        public static async Task<ImbleDevice> ConnectAsync(BluetoothLEDevice device, CancellationToken cancellationToken)
        {
            var imble = new ImbleDevice();
            await imble.Initialize(device.DeviceInformation, device.BluetoothAddress, cancellationToken);
            return imble;
        }

        /// <summary>
        /// Data has arrived.
        /// </summary>
        public event EventHandler<DataArrivedEventArgs> DataArrived;

        /// <summary>
        /// Gets an instance of DeviceInformation corresponding to this IMBLE device.
        /// </summary>
        public DeviceInformation DeviceInformation => this.bleDevice.DeviceInformation;

        /// <summary>
        /// Gets current status of this object.
        /// </summary>
        public ImbleDeviceStatus Status
        {
            get { return this.status; }
            private set
            {
                if (value == this.status) return;
                this.status = value;
                this.OnPropertyChanged();
            }
        }
        public BluetoothConnectionStatus ConnectionStatus
        {
            get { return this.connectionStatus; }
            private set
            {
                if (value == this.connectionStatus) return;
                this.connectionStatus = value;
                this.OnPropertyChanged();
            }
        }

        private readonly CompositeDisposable disposables = new CompositeDisposable();

        private BluetoothLEDevice bleDevice;
        private GattDeviceService service;
        private GattCharacteristic readCharacteristic;
        private GattCharacteristic writeCharacteristic;

        private ImbleDeviceStatus status;
        private BluetoothConnectionStatus connectionStatus;

        private readonly SynchronizationContext propertyChangedContext;

        private ImbleDevice()
        {
            this.propertyChangedContext = SynchronizationContext.Current;
            this.Status = ImbleDeviceStatus.Initializing;
        }

        /// <summary>
        /// Initialize the device.
        /// </summary>
        /// <param name="deviceInformation">An instance of DeviceInformation class corresponding to the BluetoothLEDevice.</param>
        /// <param name="bluetoothAddress">The bluetooth address of the device.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task Initialize(DeviceInformation deviceInformation, ulong bluetoothAddress, CancellationToken cancellationToken)
        {
            if (deviceInformation.Pairing.IsPaired)
            {
                // If the device is paired, all we hoave to do is just getting BluetoothLEDevice object by its ID.
                this.bleDevice = (await BluetoothLEDevice.FromIdAsync(deviceInformation.Id)).AddTo(this.disposables);
                this.service = this.bleDevice.GetGattService(ServiceUuid).AddTo(this.disposables);
            }
            else
            {
                // If the device is not paired, pair with the device.
                var result = await deviceInformation.Pairing.PairAsync(DevicePairingProtectionLevel.None);
                switch(result.Status)
                {
                    case DevicePairingResultStatus.Paired:
                        // The device has been paired successfully.
                        break;
                    default:
                        throw new ImbleOperationException("Failed to pair with the device.");
                }

                // After the PairAsync method returns, we have to wait until the device paired is registered to the system.
                var selector = BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(bluetoothAddress);
                var watcher = DeviceInformation.CreateWatcher(selector);
                var deviceAddedTask = EventSubscription.ReceiveFirst<DeviceWatcher, DeviceInformation>(handler => watcher.Added += handler, handler => watcher.Added -= handler, cancellationToken);
                watcher.Start();
                var bleDeviceInformation = await deviceAddedTask;   // Wait until the target device is added.
                watcher.Stop();

                this.bleDevice = (await BluetoothLEDevice.FromIdAsync(bleDeviceInformation.Id)).AddTo(this.disposables);
                var gattServiceChangedTask = EventSubscription.ReceiveFirst<BluetoothLEDevice, object>(handler => this.bleDevice.GattServicesChanged += handler, handler => this.bleDevice.GattServicesChanged -= handler, cancellationToken);

                this.service = this.bleDevice.GetGattService(ServiceUuid);
                if(this.service == null)
                {
                    // If the GATT services have not been enumerated yet, wait until the enumeration completes.
                    await gattServiceChangedTask;
                    this.service = this.bleDevice.GetGattService(ServiceUuid);
                }
            }
            
            // Get the READ characteristic in the IMBLE service.
            this.readCharacteristic = this.service.GetCharacteristics(ReadCharacteristicUuid).Single();
            EventSubscription.Subscribe<GattCharacteristic, GattValueChangedEventArgs>(
                handler => this.readCharacteristic.ValueChanged += handler,
                handler => this.readCharacteristic.ValueChanged -= handler,
                (sender, args) =>
                {
                    if (args.CharacteristicValue.Length < 4) return; // The length of data is too short.

                    var data = args.CharacteristicValue.ToArray();
                    var length = data[0] + 1;
                    if (data.Length < length) return; // The length field is invalid. Ignore this data.

                    var body = new byte[length - 4];
                    Array.Copy(data, 4, body, 0, body.Length);
                    this.DataArrived?.Invoke(this, new DataArrivedEventArgs(body, args.Timestamp));
                })
                .AddTo(this.disposables);

            // Enable notification of the READ characteristic.
            var readCccdResult = await this.readCharacteristic.ReadClientCharacteristicConfigurationDescriptorAsync().AsTask(cancellationToken);
            var readCccd = readCccdResult.ClientCharacteristicConfigurationDescriptor | GattClientCharacteristicConfigurationDescriptorValue.Notify;
            for (var retryCount = 0;; retryCount++)
            {
                try
                {
                    using (var timeoutCancel = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    using(var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancel.Token))
                    {
                        await this.readCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(readCccd).AsTask(linkedTokenSource.Token);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    if (retryCount > 3) throw new ImbleOperationException("Failed to configure the device.", ex);
                }
            }

            this.writeCharacteristic = this.service.GetCharacteristics(WriteCharacteristicUuid).Single();

            EventSubscription.Subscribe<TypedEventHandler<BluetoothLEDevice, object>>(
                handler => this.bleDevice.ConnectionStatusChanged += handler,
                handler => this.bleDevice.ConnectionStatusChanged -= handler,
                (device, _) =>
                {
                    this.ConnectionStatus = device.ConnectionStatus;
                })
                .AddTo(this.disposables);

            this.ConnectionStatus = this.service.Device.ConnectionStatus;
            
            this.Status = ImbleDeviceStatus.Running;
            
        }

        /// <summary>
        /// Send data to this device.
        /// </summary>
        /// <param name="data">A byte array which contains the data to send.</param>
        /// <param name="offset">An offset from the head of the byte array specified by <paramref name="data"/>. </param>
        /// <param name="length">The length of the data to send. This parameter must be less than or equal to MaxLengthOfData field.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task SendAsync(byte[] data, int offset, int length, CancellationToken cancellationToken)
        {
            if( this.Status != ImbleDeviceStatus.Running) throw new InvalidOperationException();
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || MaxLengthOfData < length) throw new ArgumentOutOfRangeException(nameof(length));
            if (data.Length < offset + length) throw new ArgumentOutOfRangeException(nameof(length));

            var dataWithHeader = new byte[MaxLengthOfData + 4];
            dataWithHeader[0] = (byte) (length + 4 - 1);
            Array.Copy(data, offset, dataWithHeader, 4, length);
            await this.writeCharacteristic.WriteValueAsync(dataWithHeader.AsBuffer(), GattWriteOption.WriteWithResponse).AsTask(cancellationToken);
        }

        /// <summary>
        /// Unpair with this device.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <remarks>
        /// After calling this method, all methods in this class except Dispose can not be called.
        /// </remarks>
        public Task UnpairAsync(CancellationToken cancellationToken)
        {
            return this.bleDevice.DeviceInformation.Pairing.UnpairAsync().AsTask(cancellationToken);
        }

        public void Dispose()
        {
            this.disposables.Dispose();
            this.bleDevice = null;
            this.service = null;
            this.readCharacteristic = null;
            this.writeCharacteristic = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            try
            {
                if (this.propertyChangedContext == null)
                {
                    this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
                else
                { 
                    this.propertyChangedContext.Post(_ => { this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }, null);
                }
            }
            catch (COMException)
            {
                // Ignore COMException due to marshaling failure.
            }
        }
    }
}
