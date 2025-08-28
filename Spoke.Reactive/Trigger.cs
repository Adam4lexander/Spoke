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
        SpokePool<List<long>> longListPool = SpokePool<List<long>>.Create(l => l.Clear());
        SpokePool<List<Subscription>> subListPool = SpokePool<List<Subscription>>.Create(l => l.Clear());
        
        List<Subscription> subs = new List<Subscription>();
        Queue<T> events = new Queue<T>(); // Event queue in case of re-entrant invokes
        Action<long> _unsub;
        Action _flush;
        long idCount = 0; // Monotonically increasing id for subscriptions
        bool isFlushing;

        public Trigger() {
            // Capture Actions once to avoid allocations
            _unsub = Unsub;
            _flush = Flush; 
        }

        /// <summary>Subscribes the trigger, without taking payload, returns unsubscribe handle</summary>
        public override SpokeHandle Subscribe(Action action) {
            return Subscribe(Subscription.Create(idCount++, action));
        }

        /// <summary>Subscribes the trigger, taking payload of type T, returns unsubscribe handle</summary>
        public SpokeHandle Subscribe(Action<T> action) {
            return Subscribe(Subscription.Create(idCount++, action));
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

        // Flush the event queue, invoking all subscribers for each event
        void Flush() {
            if (isFlushing) return;
            isFlushing = true;
            while (events.Count > 0) {
                var evt = events.Dequeue();
                // Copy subscribers, in case of modifications during invoke
                var subList = subListPool.Get();
                foreach (var sub in subs) {
                    subList.Add(sub);
                }
                foreach (var sub in subList) {
                    try { 
                        sub.Invoke(evt); 
                    } catch (Exception ex) { 
                        SpokeError.Log("Trigger subscriber error", ex); 
                    }
                }
                subListPool.Return(subList);
            }
            isFlushing = false;
        }

        void Unsub(Delegate action) {
            var idList = longListPool.Get();
            foreach (var sub in subs) {
                if (sub.Key == action) {
                    idList.Add(sub.Id);
                }
            }
            foreach (var id in idList) {
                Unsub(id);
            }
            longListPool.Return(idList);
        }

        protected override void Unsub(long id) {
            for (int i = 0; i < subs.Count; i++) {
                if (subs[i].Id == id) { 
                    subs.RemoveAt(i); 
                    return; 
                }
            }
        }

        SpokeHandle Subscribe(Subscription sub) {
            subs.Add(sub);
            return SpokeHandle.Of(sub.Id, _unsub);
        }

        // Internal representation of a subscription, either with or without payload
        struct Subscription {
            public long Id; 
            Action<T> ActionT; 
            Action Action;

            public static Subscription Create(long id, Action<T> action) {
                return new Subscription { Id = id, ActionT = action };
            }

            public static Subscription Create(long id, Action action) {
                return new Subscription { Id = id, Action = action };
            }
            
            public Delegate Key => ActionT != null ? (Delegate)ActionT : Action;
            
            public void Invoke(T arg) {
                if (ActionT != null) {
                    ActionT(arg);
                } else {
                    Action?.Invoke();
                }
            }
        }
    }
}