using System;
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

        public UnconnectedDeviceViewModel(UnconnectedImbleDevice device)
        {
            this.Device = device;

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