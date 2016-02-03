using System;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using ImbleCommunication;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Reactive.Bindings.Notifiers;

namespace HelloImble
{
    public class DevicePageViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        public ReadOnlyReactiveProperty<ImbleDevice> Device { get; }
        public ReadOnlyReactiveProperty<bool> IsConnected { get; set; }
        public ReadOnlyReactiveProperty<ImbleDeviceStatus> DeviceStatus { get; set; }
        public ReadOnlyReactiveProperty<bool> CanSendCommand { get; set; }
        public ReactiveCommand SendCommand { get; set; }
        public ReactiveProperty<string> Name { get; set; }
        public ReadOnlyReactiveProperty<ReceivedMessageViewModel> ReceivedMessage { get; }

        public DevicePageViewModel(UnconnectedImbleDevice unconnectedDevice)
        {
            this.Device = Observable.FromAsync(token => unconnectedDevice.Connect(token))
                .CatchIgnore((Exception e) => { })
                .ToReadOnlyReactiveProperty()
                .AddTo(this.disposables);

            this.IsConnected = this.Device.Select(device => device != null).ToReadOnlyReactiveProperty().AddTo(this.disposables);

            this.DeviceStatus = this.Device
                .Where(device => device != null)
                .Select(device => device.ObserveProperty(self => self.Status))
                .Switch()
                .Do(value => Debug.WriteLine(value))
                .ObserveOnUIDispatcher()
                .ToReadOnlyReactiveProperty()
                .AddTo(this.disposables);

            this.ReceivedMessage = this.Device
                .Where(device => device != null)
                .Select(device => Observable.FromEventPattern<DataArrivedEventArgs>(handler => device.DataArrived += handler, handler => device.DataArrived -= handler))
                .Switch()
                .Select(args => new ReceivedMessageViewModel(args.EventArgs.Data, args.EventArgs.Timestamp))
                .OnErrorRetry()
                .ToReadOnlyReactiveProperty()
                .AddTo(this.disposables);

            var notBusyNotifier = new BooleanNotifier(false);
            
            this.Name = new ReactiveProperty<string>("Fuga")
                .SetValidateNotifyError(value => value != null && Encoding.UTF8.GetByteCount(value) < 12 ? null : "Input short message")
                .AddTo(this.disposables);

            this.CanSendCommand = Observable.CombineLatest(
                this.DeviceStatus.Select(status => status == ImbleDeviceStatus.Running),
                this.Name.ObserveHasErrors,
                notBusyNotifier.Do(value => Debug.WriteLine(value)),
                (isRunning, hasErrors, notBusy) => isRunning && !hasErrors && notBusy)
                .ToReadOnlyReactiveProperty().AddTo(this.disposables);
            notBusyNotifier.TurnOn();

            this.SendCommand = this.CanSendCommand.ToReactiveCommand().AddTo(this.disposables);
            this.SendCommand
                .Do(_ => notBusyNotifier.TurnOff())
                .Select(_ =>
                {
                    var data = Encoding.UTF8.GetBytes(this.Name.Value);
                        return Observable.FromAsync(token =>
                        {
                            using (var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                            using (var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutTokenSource.Token))
                            {
                                return this.Device.Value.SendAsync(data, 0, data.Length, linkedTokenSource.Token);
                            }
                        })
                        .Finally(() => notBusyNotifier.TurnOn());
                })
                .Switch()
                .OnErrorRetry((Exception e) => Debug.WriteLine(e))
                .Subscribe()
                .AddTo(this.disposables);
        }

        public void Dispose()
        {
            this.Device.Value?.Dispose();
            this.disposables.Dispose();
        }
    }
}