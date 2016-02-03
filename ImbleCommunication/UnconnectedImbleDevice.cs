using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace ImbleCommunication
{
    /// <summary>
    /// Unconnected (and advertising) IMBLE device.
    /// </summary>
    public class UnconnectedImbleDevice
    {
        /// <summary>
        /// Gets Bluetooth address of this device.
        /// </summary>
        public ulong Address { get; }
        /// <summary>
        /// Gets the advertisement data this object was created from.
        /// </summary>
        public BluetoothLEAdvertisement Advertisement { get; }
        /// <summary>
        /// Gets RSSI of the advertisement signal from this device.
        /// </summary>
        public short Rssi { get; }

        public UnconnectedImbleDevice(ulong address, BluetoothLEAdvertisement advertisement, short rssi)
        {
            this.Address = address;
            this.Advertisement = advertisement;
            this.Rssi = rssi;
        }

        /// <summary>
        /// Connect to this device.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>An ImbleDevice object to communicate with this device.</returns>
        public async Task<ImbleDevice> Connect(CancellationToken cancellationToken)
        {
            return await ImbleDevice.ConnectAsync(this, cancellationToken);
        }
    }
}