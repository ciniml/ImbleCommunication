using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ImbleCommunication;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Reactive.Bindings.Notifiers;

namespace HelloImble
{
    public class UnconnectedDeviceViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new CompositeDisposable();

        public UnconnectedImbleDevice Device { get; }

        public ReactiveCommand ConnectCommand { get; }

        public string Name { get; }
        public string Address { get; }
        public short Rssi { get; }

        public UnconnectedDeviceViewModel(UnconnectedImbleDevice device)
        {
            this.Device = device;

            this.Name = this.Device.Advertisement.LocalName;

            var addressBytes = BitConverter.GetBytes(this.Device.Address);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(addressBytes);
            }
            this.Address = string.Join(":", addressBytes.Select(@byte => @byte.ToString("X02")));
            this.Rssi = this.Device.Rssi;

            this.ConnectCommand = new ReactiveCommand();
            this.ConnectCommand
                .Select(_ => Observable.FromAsync(token => this.Device.Connect(token)))
                .Switch()
                .OnErrorRetry()
                .Subscribe()
                .AddTo(this.disposables);

        }

        public void Dispose()
        {
            this.disposables.Dispose();
        }
    }
}