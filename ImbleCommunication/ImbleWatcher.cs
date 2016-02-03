using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Foundation;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace ImbleCommunication
{
    public class ImbleWatcher : IDisposable
    {
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly BluetoothLEAdvertisementWatcher watcher;
        private readonly Subject<Unit> resetSubject;

        /// <summary>
        /// Gets a collection of unconnected devices.
        /// </summary>
        public ReadOnlyReactiveCollection<UnconnectedImbleDevice> UnconnectedDevices { get; }

        /// <summary>
        /// Type number of Service Solicitation advertisement data.
        /// From table 18.9 in Volume 3 Part C Chapter 18 Section 9 of Core Spec.
        /// </summary>
        private const byte ServiceSolicitationADType = 0x15;

        /// <summary>
        /// Check the advertisement data section is Service Solicitation and contains the specific UUID.
        /// </summary>
        /// <param name="section">An instance of BluetoothLEAdvertisementDataSection to check whether it contains the specified UUID.</param>
        /// <param name="uuid">The UUID which the advertisement data is expected to contain.</param>
        /// <returns></returns>
        private static bool MatchSolicitationServiceLong(BluetoothLEAdvertisementDataSection section, Guid uuid)
        {
            // We have to reverse the first 3 parts of an UUID contained by the AD to compare with another UUID represented by Guid object.
            var rawUuidBytes = section.Data.ToArray().Reverse().ToArray();
            var uuidBytes = new[]
                {
                    rawUuidBytes.Take(4).Reverse(),
                    rawUuidBytes.Skip(4).Take(2).Reverse(),
                    rawUuidBytes.Skip(6).Take(2).Reverse(),
                    rawUuidBytes.Skip(8)
                }
                .Aggregate((l, r) => l.Concat(r))
                .ToArray();
                
                
            return section.DataType == ServiceSolicitationADType && uuidBytes.SequenceEqual(uuid.ToByteArray());
        }

        public ImbleWatcher()
        {
            this.watcher = new BluetoothLEAdvertisementWatcher();
            this.watcher.ScanningMode = BluetoothLEScanningMode.Active;     // We have to perform active scanning to check scan responses from devices.
            this.resetSubject = new Subject<Unit>().AddTo(this.disposables);
            
            var candidateAddresses = new ConcurrentDictionary<ulong, ulong>();
            var resetObservable = this.resetSubject
                .Do(_ => candidateAddresses.Clear());

            var receivedObservable = Observable.FromEvent<TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs>, BluetoothLEAdvertisementReceivedEventArgs>(
                handler => (sender, args) => handler(args),
                handler => this.watcher.Received += handler,
                handler => this.watcher.Received -= handler)
                .Publish();

            // Check scan responses and add their address to the candidate device list if they contains the target service UUID as Service Solicitation data.
            receivedObservable
                .Where(args => args.AdvertisementType.HasFlag(BluetoothLEAdvertisementType.ScanResponse))
                .Where(args => args.Advertisement.DataSections.Any(section => MatchSolicitationServiceLong(section, ImbleDevice.ServiceUuid)))
                .Subscribe(args => candidateAddresses.TryAdd(args.BluetoothAddress, args.BluetoothAddress))
                .AddTo(this.disposables);

            // Check advertisement data 
            this.UnconnectedDevices = receivedObservable
                .Where(args => !args.AdvertisementType.HasFlag(BluetoothLEAdvertisementType.ScanResponse))
                .Where(args => candidateAddresses.ContainsKey(args.BluetoothAddress))
                .Distinct(args => args.BluetoothAddress)
                .Select(args => new UnconnectedImbleDevice(args.BluetoothAddress, args.Advertisement, args.RawSignalStrengthInDBm))
                .ToReadOnlyReactiveCollection(resetObservable)
                .AddTo(this.disposables);

            receivedObservable.Connect().AddTo(this.disposables);

            this.watcher.Start();
        }

        /// <summary>
        /// Refresh the device collections.
        /// </summary>
        public void Refresh()
        {
            if (this.watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            {
                this.watcher.Stop();
            }
            this.resetSubject.OnNext(Unit.Default);
            this.watcher.Start();
        }

        public void Dispose()
        {
            this.disposables.Dispose();
        }
    }
}