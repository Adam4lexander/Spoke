// Spoke.Reactive.cs
// -----------------------------
// > Trigger
// > State
// > BaseEffect
// > Effect
// > Reaction
// > Phase
// > Effect<T>
// > Memo
// > Dock
// > SpokeEngine
// > Computation

using System;
using System.Collections.Generic;

namespace Spoke {

    public delegate void EffectBlock(EffectBuilder s);
    public delegate T EffectBlock<T>(EffectBuilder s);

    // ============================== Trigger ============================================================
    public interface ITrigger {
        SpokeHandle Subscribe(Action action);
        void Unsubscribe(Action action);
    }
    public interface ITrigger<T> : ITrigger {
        SpokeHandle Subscribe(Action<T> action);
        void Unsubscribe(Action<T> action);
    }
    public abstract class Trigger : ITrigger {
        public struct Unit { }
        public static Trigger Create() => Create<Unit>();
        public static Trigger<T> Create<T>() => new Trigger<T>();
        public abstract SpokeHandle Subscribe(Action action);
        public abstract void Invoke();
        public abstract void Unsubscribe(Action action);
        protected abstract void Unsub(long id);
    }
    public class Trigger<T> : Trigger, ITrigger<T>, IDeferredTrigger {
        List<Subscription> subs = new List<Subscription>();
        SpokePool<List<long>> longListPool = SpokePool<List<long>>.Create(l => l.Clear());
        SpokePool<List<Subscription>> subListPool = SpokePool<List<Subscription>>.Create(l => l.Clear());
        Queue<T> events = new Queue<T>();
        DeferredQueue deferred = new DeferredQueue();
        Action<long> _Unsub; Action _Flush;
        long idCount = 0;
        public Trigger() { _Unsub = Unsub; _Flush = Flush; }
        public override SpokeHandle Subscribe(Action action) => Subscribe(Subscription.Create(idCount++, action));
        public SpokeHandle Subscribe(Action<T> action) => Subscribe(Subscription.Create(idCount++, action));
        public override void Invoke() => Invoke(default(T));
        public void Invoke(T param) { events.Enqueue(param); deferred.Enqueue(_Flush); }
        public override void Unsubscribe(Action action) => Unsub(action);
        public void Unsubscribe(Action<T> action) => Unsub(action);
        void Flush() {
            while (events.Count > 0) {
                var evt = events.Dequeue();
                var subList = subListPool.Get();
                foreach (var sub in subs) subList.Add(sub);
                foreach (var sub in subList) {
                    try { sub.Invoke(evt); } catch (Exception ex) { SpokeError.Log("Trigger subscriber error", ex); }
                }
                subListPool.Return(subList);
            }
        }
        void IDeferredTrigger.OnAfterNotify(SpokeHandle handle) => deferred.Enqueue(handle);
        void Unsub(Delegate action) {
            var idList = longListPool.Get();
            foreach (var sub in subs) if (sub.Key == action) idList.Add(sub.Id);
            foreach (var id in idList) Unsub(id);
            longListPool.Return(idList);
        }
        protected override void Unsub(long id) {
            for (int i = 0; i < subs.Count; i++)
                if (subs[i].Id == id) { subs.RemoveAt(i); return; }
        }
        SpokeHandle Subscribe(Subscription sub) {
            subs.Add(sub);
            return SpokeHandle.Of(sub.Id, _Unsub);
        }
        struct Subscription {
            public long Id; Action<T> ActionT; Action Action;
            public static Subscription Create(long id, Action<T> action) => new Subscription { Id = id, ActionT = action };
            public static Subscription Create(long id, Action action) => new Subscription { Id = id, Action = action };
            public Delegate Key => ActionT != null ? (Delegate)ActionT : Action;
            public void Invoke(T arg) {
                if (ActionT != null) ActionT(arg);
                else Action?.Invoke();
            }
        }
    }
    internal interface IDeferredTrigger {
        void OnAfterNotify(SpokeHandle handle);
    }
    // ============================== State ============================================================
    public interface IRef<T> {
        T Now { get; }
    }
    public interface ISignal<T> : IRef<T>, ITrigger<T> { }
    public interface IState<T> : ISignal<T> {
        void Set(T value);
        void Update(Func<T, T> setter);
    }
    public static class State {
        public static State<T> Create<T>(T val = default) => new State<T>(val);
    }
    public class State<T> : IState<T>, IDeferredTrigger {
        T value;
        Trigger<T> trigger = new Trigger<T>();
        public State() { }
        public State(T value) { Set(value); }
        public T Now => value;
        public SpokeHandle Subscribe(Action action) => trigger.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => trigger.Subscribe(action);
        public void Unsubscribe(Action action) => trigger.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => trigger.Unsubscribe(action);
        void IDeferredTrigger.OnAfterNotify(SpokeHandle handle) => (trigger as IDeferredTrigger).OnAfterNotify(handle);
        public void Set(T value) {
            if (EqualityComparer<T>.Default.Equals(value, this.value)) return;
            this.value = value;
            trigger.Invoke(value);
        }
        public void Update(Func<T, T> setter) { if (setter != null) Set(setter(Now)); }
    }
    // ============================== BaseEffect ============================================================
    public interface EffectBuilder {
        SpokeEngine Engine { get; }
        void Log(string msg);
        T D<T>(ISignal<T> signal);
        void Use(SpokeHandle trigger);
        T Use<T>(T disposable) where T : IDisposable;
        T Call<T>(T identity) where T : Epoch;
        void Call(EpochBlock block);
        public bool TryGetLexical<T>(out T context) where T : Epoch;
        void OnCleanup(Action cleanup);
    }
    public static partial class EffectBuilderExtensions {
        public static void Subscribe(this EffectBuilder s, ITrigger trigger, Action action) => s.Use(trigger != null ? trigger.Subscribe(action) : default);
        public static void Subscribe<T>(this EffectBuilder s, ITrigger<T> trigger, Action<T> action) => s.Use(trigger != null ? trigger.Subscribe(action) : default);
        public static ISignal<T> Memo<T>(this EffectBuilder s, Func<MemoBuilder, T> selector, params ITrigger[] triggers) => s.Call(new Memo<T>("Memo", selector, triggers));
        public static ISignal<T> Memo<T>(this EffectBuilder s, string name, Func<MemoBuilder, T> selector, params ITrigger[] triggers) => s.Call(new Memo<T>(name, selector, triggers));
        public static ISignal<T> Effect<T>(this EffectBuilder s, EffectBlock<IRef<T>> block, params ITrigger[] triggers) => s.Call(new Effect<T>("Effect", block, triggers));
        public static ISignal<T> Effect<T>(this EffectBuilder s, string name, EffectBlock<IRef<T>> block, params ITrigger[] triggers) => s.Call(new Effect<T>(name, block, triggers));
        public static void Effect(this EffectBuilder s, EffectBlock buildLogic, params ITrigger[] triggers) => s.Call(new Effect("Effect", buildLogic, triggers));
        public static void Effect(this EffectBuilder s, string name, EffectBlock buildLogic, params ITrigger[] triggers) => s.Call(new Effect(name, buildLogic, triggers));
        public static void Reaction(this EffectBuilder s, EffectBlock block, params ITrigger[] triggers) => s.Call(new Reaction("Reaction", block, triggers));
        public static void Reaction(this EffectBuilder s, string name, EffectBlock block, params ITrigger[] triggers) => s.Call(new Reaction(name, block, triggers));
        public static void Phase(this EffectBuilder s, ISignal<bool> mountWhen, EffectBlock buildLogic, params ITrigger[] triggers) => s.Call(new Phase("Phase", mountWhen, buildLogic, triggers));
        public static void Phase(this EffectBuilder s, string name, ISignal<bool> mountWhen, EffectBlock buildLogic, params ITrigger[] triggers) => s.Call(new Phase(name, mountWhen, buildLogic, triggers));
        public static Dock Dock(this EffectBuilder s) => s.Call(new Dock("Dock"));
        public static Dock Dock(this EffectBuilder s, string name) => s.Call(new Dock(name));
    }
    public abstract class BaseEffect : Computation {
        protected EffectBlock block;
        EffectBuilderImpl builder;
        public BaseEffect(string name, IEnumerable<ITrigger> triggers) : base(name, triggers) {
            builder = new EffectBuilderImpl(this);
        }
        protected override void OnRun(EpochBuilder s) => builder.Mount(s, block);
        class EffectBuilderImpl : EffectBuilder {
            BaseEffect effect;
            EpochBuilder s;
            public EffectBuilderImpl(BaseEffect owner) {
                this.effect = owner;
            }
            public void Mount(EpochBuilder s, EffectBlock block) {
                this.s = s;
                block?.Invoke(this);
            }
            public SpokeEngine Engine => effect.engine;
            public void Log(string msg) => effect.LogFlush(msg);
            public T D<T>(ISignal<T> signal) { effect.AddDynamicTrigger(signal); return signal.Now; }
            public void Use(SpokeHandle trigger) => s.Use(trigger);
            public T Use<T>(T disposable) where T : IDisposable => s.Use(disposable);
            public T Call<T>(T identity) where T : Epoch => s.Call(identity);
            public void Call(EpochBlock block) => s.Call(block);
            public bool TryGetLexical<T>(out T context) where T : Epoch => s.TryGetLexical(out context);
            public void OnCleanup(Action fn) => s.OnCleanup(fn);
        }
    }
    // ============================== Effect ============================================================
    public class Effect : BaseEffect {
        public Effect(string name, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = block;
        }
    }
    // ============================== Reaction ============================================================
    public class Reaction : BaseEffect {
        public Reaction(string name, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            var isFirst = true;
            this.block = s => { if (!isFirst) block?.Invoke(s); else isFirst = false; };
        }
    }
    // ============================== Phase ============================================================
    public class Phase : BaseEffect {
        public Phase(string name, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            AddStaticTrigger(mountWhen);
            this.block = s => { if (mountWhen.Now) block?.Invoke(s); };
        }
    }
    // ============================== Effect<T> ============================================================
    public class Effect<T> : BaseEffect, ISignal<T>, IDeferredTrigger {
        State<T> state = State.Create<T>();
        public T Now => state.Now;
        public Effect(string name, EffectBlock<IRef<T>> block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = Mount(block);
        }
        EffectBlock Mount(EffectBlock<IRef<T>> block) => s => {
            if (block == null) return;
            var result = block.Invoke(s);
            if (result is ISignal<T> signal) s.Subscribe(signal, x => state.Set(x));
            s.Call(s => state.Set(result.Now));
        };
        public SpokeHandle Subscribe(Action action) => state.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => state.Subscribe(action);
        public void Unsubscribe(Action action) => state.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => state.Unsubscribe(action);
        void IDeferredTrigger.OnAfterNotify(SpokeHandle handle) => (state as IDeferredTrigger).OnAfterNotify(handle);
    }
    // ============================== Memo ============================================================
    public class Memo<T> : Computation, ISignal<T>, IDeferredTrigger {
        State<T> state = State.Create<T>();
        public T Now => state.Now;
        Action<MemoBuilder> block;
        MemoBuilder builder;
        public Memo(string name, Func<MemoBuilder, T> selector, params ITrigger[] triggers) : base(name, triggers) {
            builder = new MemoBuilder(new MemoBuilder.Friend { AddDynamicTrigger = AddDynamicTrigger });
            block = s => { if (selector != null) state.Set(selector(s)); };
        }
        protected override void OnRun(EpochBuilder s) => block(builder);
        public SpokeHandle Subscribe(Action action) => state.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => state.Subscribe(action);
        public void Unsubscribe(Action action) => state.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => state.Unsubscribe(action);
        void IDeferredTrigger.OnAfterNotify(SpokeHandle handle) => (state as IDeferredTrigger).OnAfterNotify(handle);
    }
    public class MemoBuilder { // Concrete class for IL2CPP AOT generation
        internal struct Friend {
            public Action<ITrigger> AddDynamicTrigger;
        }
        Friend memo;
        internal MemoBuilder(Friend memo) { this.memo = memo; }
        public U D<U>(ISignal<U> signal) { memo.AddDynamicTrigger(signal); return signal.Now; }
    }
    // ============================== Dock ============================================================
    public class Dock : Epoch {
        string name;
        public override string ToString() => name ?? base.ToString();
        public Dock(string name) {
            this.name = name;
        }
        public T Call<T>(object key, T identity) where T : Epoch => CallDynamic(key, identity);
        public void Effect(object key, EffectBlock buildLogic, params ITrigger[] triggers) => Effect("Effect", key, buildLogic, triggers);
        public void Effect(string name, object key, EffectBlock buildLogic, params ITrigger[] triggers) => Call(key, new Effect(name, buildLogic, triggers));
        public void Drop(object key) => DropDynamic(key);
    }
    // ============================== SpokeEngine ============================================================
    public class SpokeEngine : ExecutionEngine {
        public static SpokeEngine Create(FlushMode flushMode, ISpokeLogger logger = null) => Node.CreateRoot(new SpokeEngine(flushMode, logger)).Epoch;
        Action<long> _releaseEffect;
        long currId;
        public SpokeEngine(FlushMode flushMode, ISpokeLogger logger = null) : base(flushMode, logger) {
            _releaseEffect = ReleaseEffect;
            FlushMode = flushMode;
        }
        public SpokeHandle Effect(string name, EffectBlock buildLogic, params ITrigger[] triggers) {
            CallDynamic(currId, new Effect(name, buildLogic, triggers));
            return SpokeHandle.Of(currId++, _releaseEffect);
        }
        void ReleaseEffect(long id) => DropDynamic(id);
        public void Batch(Action action) {
            var handle = Hold();
            try { action(); } finally { handle.Dispose(); }
        }
        public void LogBatch(string msg, Action action) => Batch(() => {
            action();
            if (HasPending) LogNextFlush(msg);
        });
        public void Flush() => BeginFlush();
        protected override bool ContinueFlush(long nPasses) {
            const long maxPasses = 1000;
            if (nPasses > maxPasses) throw new Exception("Exceed iteration limit - possible infinite loop");
            return true;
        }
    }
    // ============================== Computation ============================================================
    public abstract class Computation : Epoch {
        protected SpokeEngine engine;
        DependencyTracker tracker;
        string name;
        public override string ToString() => name ?? base.ToString();
        public Computation(string name, IEnumerable<ITrigger> triggers) {
            this.name = name;
            tracker = DependencyTracker.Create(this);
            foreach (var trigger in triggers) tracker.AddStatic(trigger);
            tracker.SyncDependencies();
            OnAttached(cleanup => {
                TryGetContext(out engine);
                cleanup(() => tracker.Dispose());
            });
            OnMounted(s => {
                tracker.BeginDynamic();
                try { OnRun(s); } finally { tracker.EndDynamic(); }
            });
        }
        protected abstract void OnRun(EpochBuilder s);
        protected void AddStaticTrigger(ITrigger trigger) { tracker.AddStatic(trigger); tracker.SyncDependencies(); }
        protected void AddDynamicTrigger(ITrigger trigger) => tracker.AddDynamic(trigger);
        protected void LogFlush(string msg) => engine.LogNextFlush(msg);
        Action ScheduleFromTrigger(ITrigger trigger, int index) => () => {
            if (index >= tracker.depIndex) return;
            var handle = engine.Hold();
            Schedule();
            (trigger as IDeferredTrigger).OnAfterNotify(handle);
        };
        struct DependencyTracker : IDisposable {
            Computation owner;
            HashSet<ITrigger> seen;
            List<(ITrigger t, SpokeHandle h)> staticHandles, dynamicHandles;
            public List<Computation> dependencies;
            public int depIndex;
            public static DependencyTracker Create(Computation owner) => new DependencyTracker {
                owner = owner,
                seen = new HashSet<ITrigger>(),
                staticHandles = new List<(ITrigger t, SpokeHandle h)>(),
                dynamicHandles = new List<(ITrigger t, SpokeHandle h)>(),
                dependencies = new List<Computation>()
            };
            public void AddStatic(ITrigger trigger) {
                if (trigger == owner || !seen.Add(trigger)) return;
                staticHandles.Add((trigger, trigger.Subscribe(owner.ScheduleFromTrigger(trigger, -1))));
            }
            public void BeginDynamic() {
                depIndex = 0;
                seen.Clear();
                foreach (var dep in staticHandles) seen.Add(dep.t);
            }
            public void AddDynamic(ITrigger trigger) {
                if (trigger == owner || !seen.Add(trigger)) return;
                if (depIndex >= dynamicHandles.Count) dynamicHandles.Add((trigger, trigger.Subscribe(owner.ScheduleFromTrigger(trigger, depIndex))));
                else if (dynamicHandles[depIndex].t != trigger) {
                    dynamicHandles[depIndex].h.Dispose();
                    dynamicHandles[depIndex] = (trigger, trigger.Subscribe(owner.ScheduleFromTrigger(trigger, depIndex)));
                }
                depIndex++;
            }
            public void EndDynamic() {
                while (dynamicHandles.Count > depIndex) {
                    dynamicHandles[dynamicHandles.Count - 1].h.Dispose();
                    dynamicHandles.RemoveAt(dynamicHandles.Count - 1);
                }
                SyncDependencies();
            }
            public void SyncDependencies() {
                dependencies.Clear();
                foreach (var trig in staticHandles) if (trig.t is Computation comp) dependencies.Add(comp);
                foreach (var trig in dynamicHandles) if (trig.t is Computation comp) dependencies.Add(comp);
            }
            public void Dispose() {
                seen.Clear();
                foreach (var handle in staticHandles) handle.h.Dispose();
                foreach (var handle in dynamicHandles) handle.h.Dispose();
                staticHandles.Clear(); dynamicHandles.Clear();
                dependencies.Clear();
            }
        }
    }
}
