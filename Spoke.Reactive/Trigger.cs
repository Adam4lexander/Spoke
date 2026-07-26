using System.Collections.Generic;
using System;

namespace Spoke {

    /// <summary>
    /// A Trigger is a simple event emitter, like event or UnityEvent, but integrated into Spoke's reactive system
    /// - Use ITrigger type to expose event subscriptions minus payload, without exposing Invoke()
    /// </summary>
    public interface ITrigger {
        SpokeHandle Subscribe(Action action);
        void Unsubscribe(Action action);
    }

    /// <summary>
    /// ITrigger interface with additional subscription methods for event payload
    /// </summary>
    public interface ITrigger<out T> : ITrigger {
        SpokeHandle Subscribe(Action<T> action);
        void Unsubscribe(Action<T> action);
    }

    /// <summary>
    /// Abstract base class for Trigger<T>
    /// - Use Trigger.Create() or Trigger.Create<T>() to create instances
    /// </summary>
    public abstract class Trigger : ITrigger {

        /// <summary>Dummy payload type for Trigger.Create()</summary>
        public struct Unit { }

        /// <summary>Creates a trigger without any event payload</summary>
        public static Trigger Create()
            => Create<Unit>();

        /// <summary>Creates a trigger with event payload of type T</summary>
        public static Trigger<T> Create<T>()
            => new Trigger<T>();

        /// <summary>Subscribes the trigger, ignoring payload, returns unsubscribe handle</summary>
        public abstract SpokeHandle Subscribe(Action action);

        /// <summary>
        /// Invokes the trigger, notifying all subscribers, both with and without payload
        /// Subscribers with payload will receive default(T) as the event argument
        /// </summary>
        public abstract void Invoke();

        /// <summary>
        /// Alternatively to disposing the SpokeHandle returned by Subscribe(), pass the subscribed action here
        /// SpokeHandle.Dispose() is preferred, as it is more efficient
        /// This method is provided for convenience and parity with typical event APIs
        /// </summary>
        public abstract void Unsubscribe(Action action);

        protected abstract void Unsub(long id);
    }

    /// <summary>
    /// Concrete implementation of Trigger with event payload of type T
    /// </summary>
    public sealed class Trigger<T> : Trigger, ITrigger<T> {
        static SpokePool<List<long>> longListPool = SpokePool<List<long>>.Create(l => l.Clear());

        // Subscriptions in subscribe order. Unsubscribing tombstones a slot (nulling its action)
        // rather than removing it; Compact() sweeps the tombstones out. Dispatches walk their own
        // copy, so the slots are free to move at any time.
        List<Subscription> subs = new List<Subscription>();
        List<Subscription> dispatchList;
        int deadCount;

        Queue<T> events = new Queue<T>();   // Event queue in case of re-entrant invokes
        Action<long> _unsub;
        Action _flush;
        long idCount = 0;   // Monotonically increasing id for subscriptions
        bool isFlushing;

        public Trigger() {
            // Capture Actions once to avoid allocations
            _unsub = Unsub;
            _flush = Flush;
        }

        /// <summary>Subscribes the trigger, without taking payload, returns unsubscribe handle</summary>
        public override SpokeHandle Subscribe(Action action) {
            return Subscribe(new Subscription { Id = idCount++, Fn = action });
        }

        /// <summary>Subscribes the trigger, taking payload of type T, returns unsubscribe handle</summary>
        public SpokeHandle Subscribe(Action<T> action) {
            return Subscribe(new Subscription { Id = idCount++, Fn = action });
        }

        /// <summary>
        /// Invokes the trigger, notifying all subscribers, both with and without payload
        /// Subscribers with payload will receive default(T) as the event argument
        /// </summary>
        public override void Invoke() {
            Invoke(default(T));
        }

        /// <summary>Invokes the trigger with event payload</summary>
        public void Invoke(T param) {
            events.Enqueue(param);
            SpokeRuntime.Batch(_flush);
        }

        /// <summary>SpokeHandle.Dispose() is preferred, as it is more efficient</summary>
        public override void Unsubscribe(Action action) {
            Unsub(action);
        }

        /// <summary>SpokeHandle.Dispose() is preferred, as it is more efficient</summary>
        public void Unsubscribe(Action<T> action) {
            Unsub(action);
        }

        // Flush the event queue, dispatching each event to the subscribers
        void Flush() {
            if (isFlushing) return;
            isFlushing = true;
            try {
                while (events.Count > 0) {
                    Dispatch(events.Dequeue());
                }
            } finally {
                isFlushing = false;
                Compact();
            }
        }

        // Notify the subscribers the trigger had when the dispatch began. The copy fixes that set:
        // anyone subscribing during the dispatch isn't notified this round, and anyone
        // unsubscribing during it was still in the set, so is notified anyway.
        void Dispatch(T evt) {
            var subList = dispatchList ?? (dispatchList = new List<Subscription>());
            subList.Clear();        // No-op unless the previous dispatch was cut short
            subList.AddRange(subs); // Tombstones come along; their null action no-ops in Invoke
            for (var i = 0; i < subList.Count; i++) {
                try {
                    subList[i].Invoke(evt);
                } catch (Exception ex) {
                    SpokeError.Log("Trigger subscriber error", ex);
                }
            }
            subList.Clear(); // Also drops the copied action refs, so the copy retains nothing
        }

        void Unsub(Delegate action) {
            var idList = longListPool.Get();
            for (var i = 0; i < subs.Count; i++) {
                var sub = subs[i];
                if (sub.Fn == action) {
                    idList.Add(sub.Id);
                }
            }
            foreach (var id in idList) {
                Unsub(id);
            }
            longListPool.Return(idList);
        }

        // Ids ascend with slot order, so the slot can be found by binary search
        protected override void Unsub(long id) {
            var lo = 0;
            var hi = subs.Count - 1;
            while (lo <= hi) {
                var mid = (int)(((uint)lo + (uint)hi) >> 1);
                var sub = subs[mid];
                if (sub.Id < id) { lo = mid + 1; continue; }
                if (sub.Id > id) { hi = mid - 1; continue; }
                if (sub.Fn == null) return;   // Already unsubscribed
                sub.Fn = null;  // An in-flight dispatch still holds its own copy
                subs[mid] = sub;
                deadCount++;
                return;
            }
        }

        // Sweep out the tombstoned slots, packing the survivors down
        void Compact() {
            // Not worth the walk until the tombstones outnumber the live slots
            if (deadCount * 2 <= subs.Count) return;
            var write = 0;
            for (var read = 0; read < subs.Count; read++) {
                if (subs[read].Fn == null) continue;
                subs[write++] = subs[read];
            }
            subs.RemoveRange(write, subs.Count - write);
            deadCount = 0;
        }

        SpokeHandle Subscribe(Subscription sub) {
            // Nothing else sweeps a trigger that gets subscribed and unsubscribed but never invoked
            Compact();
            subs.Add(sub);
            return SpokeHandle.Of(sub.Id, _unsub);
        }

        // Internal representation of a subscription, either with or without payload
        struct Subscription {
            public long Id;
            public Delegate Fn;     // Action<T> or Action. Null marks an unsubscribed slot

            public void Invoke(T arg) {
                if (Fn is Action<T> withPayload) {
                    withPayload(arg);
                } else if (Fn is Action plain) {
                    plain();
                }
            }
        }
    }
}
