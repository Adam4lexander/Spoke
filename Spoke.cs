// Spoke.cs
// -----------------------------
// > Trigger
// > State
// > BaseEffect
// > Effect
// > Reaction
// > Phase
// > Computed
// > Memo
// > Dock
// > SpokeEngine

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
        DeferredQueue deferred = DeferredQueue.Create();
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
        void IDeferredTrigger.OnAfterNotify(Action action) => deferred.Enqueue(action);
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
        void OnAfterNotify(Action action);
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
        void IDeferredTrigger.OnAfterNotify(Action action) => (trigger as IDeferredTrigger).OnAfterNotify(action);
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
        T Component<T>(T identity) where T : Facet;
        public bool TryGetAmbient<T>(out T context) where T : Facet;
        void OnCleanup(Action cleanup);
    }
    public static partial class EffectBuilderExtensions {
        public static void UseSubscribe(this EffectBuilder s, ITrigger trigger, Action action) => s.Use(trigger != null ? trigger.Subscribe(action) : default);
        public static void UseSubscribe<T>(this EffectBuilder s, ITrigger<T> trigger, Action<T> action) => s.Use(trigger != null ? trigger.Subscribe(action) : default);
        public static ISignal<T> UseMemo<T>(this EffectBuilder s, Func<MemoBuilder, T> selector, params ITrigger[] triggers) => s.Component(new Memo<T>("Memo", selector, triggers));
        public static ISignal<T> UseMemo<T>(this EffectBuilder s, string name, Func<MemoBuilder, T> selector, params ITrigger[] triggers) => s.Component(new Memo<T>(name, selector, triggers));
        public static ISignal<T> UseComputed<T>(this EffectBuilder s, EffectBlock<IRef<T>> block, params ITrigger[] triggers) => s.Component(new Computed<T>("Computed", block, triggers));
        public static ISignal<T> UseComputed<T>(this EffectBuilder s, string name, EffectBlock<IRef<T>> block, params ITrigger[] triggers) => s.Component(new Computed<T>(name, block, triggers));
        public static void UseEffect(this EffectBuilder s, EffectBlock buildLogic, params ITrigger[] triggers) => s.Component(new Effect("Effect", buildLogic, triggers));
        public static void UseEffect(this EffectBuilder s, string name, EffectBlock buildLogic, params ITrigger[] triggers) => s.Component(new Effect(name, buildLogic, triggers));
        public static void UseReaction(this EffectBuilder s, EffectBlock block, params ITrigger[] triggers) => s.Component(new Reaction("Reaction", block, triggers));
        public static void UseReaction(this EffectBuilder s, string name, EffectBlock block, params ITrigger[] triggers) => s.Component(new Reaction(name, block, triggers));
        public static void UsePhase(this EffectBuilder s, ISignal<bool> mountWhen, EffectBlock buildLogic, params ITrigger[] triggers) => s.Component(new Phase("Phase", mountWhen, buildLogic, triggers));
        public static void UsePhase(this EffectBuilder s, string name, ISignal<bool> mountWhen, EffectBlock buildLogic, params ITrigger[] triggers) => s.Component(new Phase(name, mountWhen, buildLogic, triggers));
        public static Dock UseDock(this EffectBuilder s) => s.Component(new Dock("Dock"));
        public static Dock UseDock(this EffectBuilder s, string name) => s.Component(new Dock(name));
    }
    public abstract class BaseEffect : SpokeEngine.Computation {
        protected EffectBlock block;
        EffectBuilderImpl builder;
        public BaseEffect(string name, IEnumerable<ITrigger> triggers) : base(name, triggers) {
            builder = new EffectBuilderImpl(this);
        }
        protected override void OnRun(SpokeBuilder s) => builder.Mount(s, block);
        class EffectBuilderImpl : EffectBuilder {
            BaseEffect effect;
            SpokeBuilder s;
            public EffectBuilderImpl(BaseEffect owner) {
                this.effect = owner;
            }
            public void Mount(SpokeBuilder s, EffectBlock block) {
                this.s = s;
                block?.Invoke(this);
            }
            public SpokeEngine Engine => effect.engine;
            public void Log(string msg) => effect.LogFlush(msg);
            public T D<T>(ISignal<T> signal) { effect.AddDynamicTrigger(signal); return signal.Now; }
            public void Use(SpokeHandle trigger) => s.Use(trigger);
            public T Component<T>(T identity) where T : Facet => s.Component(identity);
            public bool TryGetAmbient<T>(out T context) where T : Facet => effect.TryGetAmbient(out context);
            public void OnCleanup(Action fn) => s.OnCleanup(fn);
        }
    }
    // ============================== Effect ============================================================
    public class Effect : BaseEffect {
        public Effect(string name, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = block;
            OnAttached(_ => Schedule());
        }
    }
    // ============================== Reaction ============================================================
    public class Reaction : BaseEffect {
        public Reaction(string name, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = block;
        }
    }
    // ============================== Phase ============================================================
    public class Phase : BaseEffect {
        public Phase(string name, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            AddStaticTrigger(mountWhen);
            this.block = s => { if (mountWhen.Now) block?.Invoke(s); };
            OnAttached(_ => Schedule());
        }
    }
    // ============================== Computed ============================================================
    public class Computed<T> : BaseEffect, ISignal<T>, IDeferredTrigger {
        State<T> state = State.Create<T>();
        public T Now => state.Now;
        public Computed(string name, EffectBlock<T> block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = MountValue(block);
            OnAttached(_ => Schedule());
        }
        public Computed(string name, EffectBlock<IRef<T>> block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = MountBoxed(block);
            OnAttached(_ => Schedule());
        }
        EffectBlock MountValue(EffectBlock<T> block) => s => {
            if (block != null) state.Set(block.Invoke(s));
        };
        EffectBlock MountBoxed(EffectBlock<IRef<T>> block) => s => {
            if (block == null) return;
            var result = block.Invoke(s);
            if (result is ISignal<T> signal) s.UseSubscribe(signal, x => state.Set(x));
            s.Component(new Scope(s => state.Set(result.Now)));
        };
        class Scope : Facet { public Scope(SpokeBlock block) { OnAttached(_ => Schedule()); OnMounted(block); } }
        public SpokeHandle Subscribe(Action action) => state.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => state.Subscribe(action);
        public void Unsubscribe(Action action) => state.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => state.Unsubscribe(action);
        void IDeferredTrigger.OnAfterNotify(Action action) => (state as IDeferredTrigger).OnAfterNotify(action);
    }
    // ============================== Memo ============================================================
    public class Memo<T> : SpokeEngine.Computation, ISignal<T>, IDeferredTrigger {
        State<T> state = State.Create<T>();
        public T Now => state.Now;
        Action<MemoBuilder> block;
        MemoBuilder builder;
        public Memo(string name, Func<MemoBuilder, T> selector, params ITrigger[] triggers) : base(name, triggers) {
            builder = new MemoBuilder(new MemoBuilder.Friend { AddDynamicTrigger = AddDynamicTrigger });
            block = s => { if (selector != null) state.Set(selector(s)); };
            OnAttached(_ => Schedule());
        }
        protected override void OnRun(SpokeBuilder s) => block(builder);
        public SpokeHandle Subscribe(Action action) => state.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => state.Subscribe(action);
        public void Unsubscribe(Action action) => state.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => state.Unsubscribe(action);
        void IDeferredTrigger.OnAfterNotify(Action action) => (state as IDeferredTrigger).OnAfterNotify(action);
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
    public class Dock : Facet {
        string name;
        public override string ToString() => name ?? base.ToString();
        public Dock(string name) {
            this.name = name;
            OnAttached(cleanup => Schedule());
        }
        public T Component<T>(object key, T identity) where T : Facet => DynamicComponent(key, identity);
        public void UseEffect(object key, EffectBlock buildLogic, params ITrigger[] triggers) => UseEffect("Effect", key, buildLogic, triggers);
        public void UseEffect(string name, object key, EffectBlock buildLogic, params ITrigger[] triggers) => Component(key, new Effect(name, buildLogic, triggers));
        public void Drop(object key) => DropComponent(key);
    }
    // ============================== SpokeEngine ============================================================
    public enum FlushMode { Immediate, Manual }
    public class SpokeEngine : ExecutionEngine {
        public static SpokeEngine Create(FlushMode flushMode, ISpokeLogger logger = null) => Node.CreateRoot(new SpokeEngine(flushMode, logger)).Identity;
        public FlushMode FlushMode = FlushMode.Immediate;
        FlushLogger flushLogger = FlushLogger.Create();
        DeferredQueue deferred = DeferredQueue.Create();
        List<string> pendingLogs = new List<string>();
        ISpokeLogger logger;
        Action _flush; Action<long> _releaseEffect;
        long currId;
        public SpokeEngine(FlushMode flushMode, ISpokeLogger logger = null) {
            _flush = FlushNow;
            _releaseEffect = ReleaseEffect;
            FlushMode = flushMode;
            this.logger = logger ?? new ConsoleSpokeLogger();
        }
        public SpokeHandle UseEffect(string name, EffectBlock buildLogic, params ITrigger[] triggers) {
            DynamicComponent(currId, new Effect(name, buildLogic, triggers));
            return SpokeHandle.Of(currId++, _releaseEffect);
        }
        void ReleaseEffect(long id) => DropComponent(id);
        public void Batch(Action action) {
            deferred.Hold();
            try { action(); } finally { deferred.Release(); }
        }
        public void LogBatch(string msg, Action action) => Batch(() => {
            action();
            if (!deferred.IsEmpty) pendingLogs.Add(msg);
        });
        protected override void OnPending() {
            if (FlushMode == FlushMode.Immediate) Flush();
        }
        public void Flush() { if (deferred.IsEmpty) deferred.Enqueue(_flush); }
        void FlushNow() {
            var maxPasses = 1000; var passes = 0;
            try {
                flushLogger.OnFlushStart();
                Node prev = null;
                while (HasPending) {
                    if (passes > maxPasses) throw new Exception("Exceed iteration limit - possible infinite loop");
                    var exec = ExecuteNext();
                    flushLogger.OnFlushNode(exec);
                    if (prev != null && prev.Coords.CompareTo(exec.Coords) > 0) passes++;
                    prev = exec;
                }
                if (pendingLogs.Count > 0 || flushLogger.HasErrors) flushLogger.LogFlush(logger, string.Join(",", pendingLogs));
            } catch (Exception ex) {
                SpokeError.Log("Internal Flush Error: ", ex);
            } finally {
                pendingLogs.Clear();
            }
        }
        public abstract class Computation : Facet, IComparable<Computation> {
            protected SpokeEngine engine;
            DependencyTracker tracker;
            string name;
            public ReadOnlyList<Computation> Dependencies => new ReadOnlyList<Computation>(tracker.dependencies);
            public override string ToString() => name ?? base.ToString();
            public int CompareTo(Computation other) => Coords.CompareTo(other.Coords);
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
            protected abstract void OnRun(SpokeBuilder s);
            protected void AddStaticTrigger(ITrigger trigger) { tracker.AddStatic(trigger); tracker.SyncDependencies(); }
            protected void AddDynamicTrigger(ITrigger trigger) => tracker.AddDynamic(trigger);
            protected void LogFlush(string msg) => engine.pendingLogs.Add(msg);
            Action ScheduleFromTrigger(ITrigger trigger, int index) => () => {
                if (index >= tracker.depIndex) return;
                engine.deferred.Hold();
                Schedule();
                (trigger as IDeferredTrigger).OnAfterNotify(() => engine.deferred.Release());
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
}