using System.Collections.Generic;
using System;

namespace Spoke {

    public interface ITrigger {
        SpokeHandle Subscribe(Action action);
        void Unsubscribe(Action action);
    }

    public interface ITrigger<out T> : ITrigger {
        SpokeHandle Subscribe(Action<T> action);
        void Unsubscribe(Action<T> action);
    }

    public abstract class Trigger : ITrigger {

        public struct Unit { }

        public static Trigger Create() 
            => Create<Unit>();

        public static Trigger<T> Create<T>() 
            => new Trigger<T>();

        public abstract SpokeHandle Subscribe(Action action);

        public abstract void Invoke();

        public abstract void Unsubscribe(Action action);

        protected abstract void Unsub(long id);
    }

    public class Trigger<T> : Trigger, ITrigger<T> {
        SpokePool<List<long>> longListPool = SpokePool<List<long>>.Create(l => l.Clear());
        SpokePool<List<Subscription>> subListPool = SpokePool<List<Subscription>>.Create(l => l.Clear());
        
        List<Subscription> subs = new List<Subscription>();
        Queue<T> events = new Queue<T>();
        Action<long> _unsub;
        Action _flush;
        long idCount = 0;
        bool isFlushing;

        public Trigger() { 
            _unsub = Unsub; 
            _flush = Flush; 
        }

        public override SpokeHandle Subscribe(Action action) {
            return Subscribe(Subscription.Create(idCount++, action));
        }

        public SpokeHandle Subscribe(Action<T> action) {
            return Subscribe(Subscription.Create(idCount++, action));
        }

        public override void Invoke() {
            Invoke(default(T));
        }

        public void Invoke(T param) { 
            events.Enqueue(param); 
            SpokeRuntime.Batch(_flush); 
        }

        public override void Unsubscribe(Action action) {
            Unsub(action);
        }

        public void Unsubscribe(Action<T> action) {
            Unsub(action);
        }

        void Flush() {
            if (isFlushing) return;
            isFlushing = true;
            while (events.Count > 0) {
                var evt = events.Dequeue();
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