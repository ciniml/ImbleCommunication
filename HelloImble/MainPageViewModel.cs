using System;
using System.Reactive.Disposables;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace HelloImble
{
    public class MainPageViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        public ReadOnlyReactiveCollection<UnconnectedDeviceViewModel> UnconnectedDevices { get; }

        public MainPageViewModel()
        {
            var watcher = ((App) App.Current).Watcher;
            this.UnconnectedDevices = watcher.UnconnectedDevices.ToReadOnlyReactiveCollection(device => new UnconnectedDeviceViewModel(device)).AddTo(this.disposables);
        }


        public void Dispose()
        {
            this.disposables.Dispose();
        }
    }
}