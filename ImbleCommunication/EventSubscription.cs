using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace ImbleCommunication
{
    class EventSubscription : IDisposable
    {
        public static EventSubscription Subscribe<TEventHandler>(Action<TEventHandler> addHandler, Action<TEventHandler> removeHandler, TEventHandler handler)
        {
            addHandler(handler);
            return new EventSubscription(() => removeHandler(handler));
        }

        public static EventSubscription Subscribe<TSender, TResult>(Action<TypedEventHandler<TSender, TResult>> addHandler, Action<TypedEventHandler<TSender, TResult>> removeHandler, TypedEventHandler<TSender, TResult> handler)
        {
            addHandler(handler);
            return new EventSubscription(() => removeHandler(handler));
        }

        public static async Task<TResult> ReceiveFirst<TSender, TResult>(Action<TypedEventHandler<TSender, TResult>> addHandler, Action<TypedEventHandler<TSender, TResult>> removeHandler, CancellationToken cancellationToken)
        {
            var task = new TaskCompletionSource<TResult>();
            using (Subscribe(addHandler, removeHandler, (sender, args) => task.TrySetResult(args)))
            using(cancellationToken.Register(() => task.TrySetCanceled(cancellationToken)))
            {
                return await task.Task;
            }
        }

        private Action removeAction;
        private EventSubscription(Action removeAction)
        {
            this.removeAction = removeAction;
        }

        public void Dispose()
        {
            this.removeAction?.Invoke();
            this.removeAction = null;
        }
    }
}
