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

        // Internal methods used by the reactive objects that depend on a trigger: effects and memos.
        internal interface Friend {
            SpokeHandle AddDependant(Action action);
        }

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
    public sealed class Trigger<T> : Trigger, ITrigger<T>, Trigger.Friend {
        // Subscribers, in subscribe order. A dispatch walks its own copy, so they're free to come
        // and go while it runs.
        SlotList subs = new SlotList();
        List<Subscription> dispatchList;

        // Dependants are the reactive objects that depend on this trigger. They're kept apart from
        // the subscribers, because they're told first: the moment the trigger is invoked, and
        // before any subscriber is called. All a dependant does about it is mark or schedule
        // itself. So by the time a subscriber's callback runs, everything that depends on the
        // trigger already knows it fired, and no callback can catch a memo that's out of date.
        // Null until the first is added.
        SlotList dependants;
        int notifyDepth;
        Action<long> _removeDependant;

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
            subs.Remove(action);
        }

        /// <summary>SpokeHandle.Dispose() is preferred, as it is more efficient</summary>
        public void Unsubscribe(Action<T> action) {
            subs.Remove(action);
        }

        // Tell the dependants, then flush the event queue, dispatching each event to the subscribers
        void Flush() {
            // Dependants don't wait in the queue like events do. Even a re-entrant invoke, which
            // has to wait its turn to reach the subscribers, tells the dependants right away.
            NotifyDependants();
            if (isFlushing) return;
            isFlushing = true;
            try {
                while (events.Count > 0) {
                    Dispatch(events.Dequeue());
                }
            } finally {
                isFlushing = false;
                subs.Compact();
            }
        }

        // Notify the subscribers the trigger had when the dispatch began. The copy fixes that set:
        // anyone subscribing during the dispatch isn't notified this round, and anyone
        // unsubscribing during it was still in the set, so is notified anyway.
        void Dispatch(T evt) {
            var subList = dispatchList ?? (dispatchList = new List<Subscription>());
            subList.Clear();        // No-op unless the previous dispatch was cut short
            subs.CopyTo(subList);   // Tombstones come along; their null action no-ops in Invoke
            for (var i = 0; i < subList.Count; i++) {
                try {
                    subList[i].Invoke(evt);
                } catch (Exception ex) {
                    SpokeError.Log("Trigger subscriber error", ex);
                }
            }
            subList.Clear(); // Also drops the copied action refs, so the copy retains nothing
        }

        // Dependants take no payload, so there's no event to queue or list to copy. Nothing they
        // do can change the list beyond leaving a tombstone, which keeps the indices good.
        void NotifyDependants() {
            if (dependants == null) return;
            var count = dependants.Count; // Any added during the pass aren't told this time
            notifyDepth++;
            try {
                for (var i = 0; i < count; i++) {
                    try {
                        (dependants[i].Fn as Action)?.Invoke();
                    } catch (Exception ex) {
                        SpokeError.Log("Trigger dependant error", ex);
                    }
                }
            } finally {
                notifyDepth--;
            }
        }

        SpokeHandle Friend.AddDependant(Action action) {
            if (dependants == null) {
                dependants = new SlotList();
                _removeDependant = dependants.Remove;
            }
            // Not during a pass, as a sweep would move slots it has yet to reach
            if (notifyDepth == 0) dependants.Compact();
            var id = idCount++;
            dependants.Add(new Subscription { Id = id, Fn = action });
            return SpokeHandle.Of(id, _removeDependant);
        }

        protected override void Unsub(long id) {
            subs.Remove(id); // An in-flight dispatch still holds its own copy
        }

        SpokeHandle Subscribe(Subscription sub) {
            // Nothing else sweeps a trigger that gets subscribed and unsubscribed but never invoked
            subs.Compact();
            subs.Add(sub);
            return SpokeHandle.Of(sub.Id, _unsub);
        }

        // Internal representation of a subscription, either with or without payload.
        // Also of a dependant, which is always without
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

        // Subscriptions in the order they were added. Removing one tombstones its slot (nulling
        // its action) rather than moving the others, and Compact() sweeps the tombstones out.
        sealed class SlotList {
            List<Subscription> slots = new List<Subscription>();
            int deadCount;

            public int Count => slots.Count;
            public Subscription this[int index] => slots[index];

            public void Add(Subscription sub) => slots.Add(sub);
            public void CopyTo(List<Subscription> target) => target.AddRange(slots);

            // Ids ascend with slot order, so the slot can be found by binary search
            public void Remove(long id) {
                var lo = 0;
                var hi = slots.Count - 1;
                while (lo <= hi) {
                    var mid = (int)(((uint)lo + (uint)hi) >> 1);
                    var slot = slots[mid];
                    if (slot.Id < id) { lo = mid + 1; continue; }
                    if (slot.Id > id) { hi = mid - 1; continue; }
                    if (slot.Fn != null) Tombstone(mid);
                    return;
                }
            }

            // Removes every subscription of the given action
            public void Remove(Delegate action) {
                if (action == null) return;
                for (var i = 0; i < slots.Count; i++) {
                    if (slots[i].Fn == action) Tombstone(i);
                }
            }

            // Sweeps out the tombstones, packing the survivors down
            public void Compact() {
                // Not worth the walk until the tombstones outnumber the live slots
                if (deadCount * 2 <= slots.Count) return;
                var write = 0;
                for (var read = 0; read < slots.Count; read++) {
                    if (slots[read].Fn == null) continue;
                    slots[write++] = slots[read];
                }
                slots.RemoveRange(write, slots.Count - write);
                deadCount = 0;
            }

            void Tombstone(int index) {
                var slot = slots[index];
                slot.Fn = null;
                slots[index] = slot;
                deadCount++;
            }
        }
    }
}
