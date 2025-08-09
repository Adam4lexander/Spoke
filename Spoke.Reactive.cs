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
// > SpokeEngine
// > Computation
// > DependencyTracker

using System;
using System.Collections.Generic;

namespace Spoke {

    public delegate void EffectBlock(EffectBuilder s);
    public delegate Ref<T> EffectBlock<T>(EffectBuilder s);
    public delegate T MemoBlock<T>(MemoBuilder s);

    // ============================== Trigger ============================================================
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
    public interface Ref<out T> {
        T Now { get; }
    }
    public interface ISignal<out T> : Ref<T>, ITrigger<T> { }
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
    public static partial class EffectBuilderExtensions {
        public static void Subscribe(this EffectBuilder s, ITrigger trigger, Action action)
            => s.Use(trigger != null ? trigger.Subscribe(action) : default);
        public static void Subscribe<T>(this EffectBuilder s, ITrigger<T> trigger, Action<T> action)
            => s.Use(trigger != null ? trigger.Subscribe(action) : default);
        public static ISignal<T> Memo<T>(this EffectBuilder s, MemoBlock<T> selector, params ITrigger[] triggers)
            => s.Call(new Memo<T>("Memo", selector, triggers));
        public static ISignal<T> Memo<T>(this EffectBuilder s, string name, MemoBlock<T> selector, params ITrigger[] triggers)
            => s.Call(new Memo<T>(name, selector, triggers));
        public static ISignal<T> Effect<T>(this EffectBuilder s, EffectBlock<T> block, params ITrigger[] triggers)
            => s.Call(new Effect<T>("Effect", block, triggers));
        public static ISignal<T> Effect<T>(this EffectBuilder s, string name, EffectBlock<T> block, params ITrigger[] triggers)
            => s.Call(new Effect<T>(name, block, triggers));
        public static void Effect(this EffectBuilder s, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Effect("Effect", block, triggers));
        public static void Effect(this EffectBuilder s, string name, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Effect(name, block, triggers));
        public static void Reaction(this EffectBuilder s, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Reaction("Reaction", block, triggers));
        public static void Reaction(this EffectBuilder s, string name, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Reaction(name, block, triggers));
        public static void Phase(this EffectBuilder s, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Phase("Phase", mountWhen, block, triggers));
        public static void Phase(this EffectBuilder s, string name, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Phase(name, mountWhen, block, triggers));
        public static Dock Dock(this EffectBuilder s)
            => s.Call(new Dock("Dock"));
        public static Dock Dock(this EffectBuilder s, string name)
            => s.Call(new Dock(name));
    }
    public static partial class DockExtensions {
        public static void Effect(this Dock dock, object key, EffectBlock block, params ITrigger[] triggers)
            => dock.Call(key, new Effect("Effect", block, triggers));
        public static void Effect(this Dock dock, string name, object key, EffectBlock block, params ITrigger[] triggers)
            => dock.Call(key, new Effect(name, block, triggers));
    }
    public abstract class BaseEffect : Computation {
        protected EffectBlock block;
        Action<ITrigger> _addDynamicTrigger;
        public BaseEffect(string name, IEnumerable<ITrigger> triggers) : base(name, triggers) {
            _addDynamicTrigger = AddDynamicTrigger;
        }
        protected override void OnRun(EpochBuilder s) => block?.Invoke(new EffectBuilder(_addDynamicTrigger, s));
    }
    public struct EffectBuilder {
        Action<ITrigger> addDynamicTrigger;
        EpochBuilder s;
        public EffectBuilder(Action<ITrigger> addDynamicTrigger, EpochBuilder s) {
            this.addDynamicTrigger = addDynamicTrigger;
            this.s = s;
        }
        public void Log(string msg) => s.Log(msg);
        public T D<T>(ISignal<T> signal) { addDynamicTrigger(signal); return signal.Now; }
        public void Use(SpokeHandle trigger) => s.Use(trigger);
        public T Use<T>(T disposable) where T : IDisposable => s.Use(disposable);
        public T Call<T>(T epoch) where T : Epoch => s.Call(epoch);
        public void Call(EpochBlock block) => s.Call(block);
        public bool TryGetLexical<T>(out T context) where T : Epoch => s.TryGetLexical(out context);
        public void OnCleanup(Action fn) => s.OnCleanup(fn);
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
        ISignal<bool> mountWhen;
        public Phase(string name, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            this.mountWhen = mountWhen;
            this.block = s => { if (mountWhen.Now) block?.Invoke(s); };
        }
        protected override ExecBlock Init(EpochBuilder s) {
            var mountBlock = base.Init(s);
            AddStaticTrigger(mountWhen);
            return mountBlock;
        }
    }
    // ============================== Effect<T> ============================================================
    public class Effect<T> : BaseEffect, ISignal<T>, IDeferredTrigger {
        State<T> state = State.Create<T>();
        public T Now => state.Now;
        public Effect(string name, EffectBlock<T> block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = Mount(block);
        }
        EffectBlock Mount(EffectBlock<T> block) => s => {
            if (block == null) return;
            var result = block.Invoke(s);
            if (result is ISignal<T> signal) s.Subscribe(signal, x => state.Set(x));
            s.Call(s => s => state.Set(result.Now));
        };
        public SpokeHandle Subscribe(Action action) => state.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => state.Subscribe(action);
        public void Unsubscribe(Action action) => state.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => state.Unsubscribe(action);
        void IDeferredTrigger.OnAfterNotify(Action action) => (state as IDeferredTrigger).OnAfterNotify(action);
    }
    // ============================== Memo ============================================================
    public class Memo<T> : Computation, ISignal<T>, IDeferredTrigger {
        State<T> state = State.Create<T>();
        Action<ITrigger> _addDynamicTrigger;
        public T Now => state.Now;
        Action<MemoBuilder> block;
        public Memo(string name, MemoBlock<T> selector, params ITrigger[] triggers) : base(name, triggers) {
            block = s => { if (selector != null) state.Set(selector(s)); };
            _addDynamicTrigger = AddDynamicTrigger;
        }
        protected override void OnRun(EpochBuilder s) {
            var builder = new MemoBuilder(_addDynamicTrigger, s);
            block(builder);
        }
        public SpokeHandle Subscribe(Action action) => state.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => state.Subscribe(action);
        public void Unsubscribe(Action action) => state.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => state.Unsubscribe(action);
        void IDeferredTrigger.OnAfterNotify(Action action) => (state as IDeferredTrigger).OnAfterNotify(action);
    }
    public struct MemoBuilder {
        Action<ITrigger> addDynamicTrigger;
        EpochBuilder s;
        internal MemoBuilder(Action<ITrigger> addDynamicTrigger, EpochBuilder s) {
            this.addDynamicTrigger = addDynamicTrigger;
            this.s = s;
        }
        public U D<U>(ISignal<U> signal) { addDynamicTrigger(signal); return signal.Now; }
        public void OnCleanup(Action fn) => s.OnCleanup(fn);
    }
    // ============================== SpokeEngine ============================================================
    public enum FlushMode { Immediate, Manual }
    public class SpokeEngine : ExecutionEngine, SpokeEngine.Friend {
        new internal interface Friend { void FastHold(); void FastRelease(); }
        public FlushMode FlushMode = FlushMode.Immediate;
        DeferredQueue deferred = new DeferredQueue();
        Action flushCommand;
        EffectBlock block;
        void Friend.FastHold() => deferred.FastHold();
        void Friend.FastRelease() => deferred.FastRelease();
        public SpokeEngine(string name, EffectBlock block, FlushMode flushMode = FlushMode.Immediate, ISpokeLogger logger = null) : base(logger) {
            Name = name;
            this.block = block;
            FlushMode = flushMode;
        }
        public SpokeEngine(EffectBlock block, FlushMode flushMode = FlushMode.Immediate, ISpokeLogger logger = null) : this("SpokeEngine", block, flushMode, logger) { }
        public void Batch(Action action) {
            var handle = deferred.Hold();
            try { action(); } finally { handle.Dispose(); }
        }
        protected override EpochBlock Bootstrap(EngineBuilder s) {
            Action _scheduleExec = s.ScheduleExec;
            var isPending = false;
            flushCommand = () => {
                if (isPending) return;
                isPending = true;
                deferred.Enqueue(_scheduleExec);
            };
            s.OnCleanup(() => flushCommand = null);
            s.OnHasPending(() => {
                if (FlushMode == FlushMode.Immediate) flushCommand.Invoke();
            });
            s.OnExec(s => {
                const long maxPasses = 1000;
                if (!isPending) return;
                isPending = false;
                var startFlush = s.FlushNumber;
                try {
                    while (s.HasPending) {
                        if (s.FlushNumber - startFlush > maxPasses) throw new Exception("Exceed iteration limit - possible infinite loop");
                        s.RunNext();
                    }
                } catch (Exception ex) { SpokeError.Log("Internal Flush Error", ex); }
            });
            return s => {
                if (block == null) return null;
                return s => s.Call(new Effect("Root", block));
            };
        }
        public void Flush() => flushCommand?.Invoke();
    }
    // ============================== Computation ============================================================
    public abstract class Computation : Epoch {
        IEnumerable<ITrigger> triggers;
        DependencyTracker tracker;
        public Computation(string name, IEnumerable<ITrigger> triggers) {
            Name = name;
            this.triggers = triggers;
        }
        protected override ExecBlock Init(EpochBuilder s) {
            s.TryGetContext<SpokeEngine>(out var engine);
            tracker = new DependencyTracker(engine, s.ScheduleExec);
            s.OnCleanup(() => tracker.Dispose());
            foreach (var trigger in triggers) tracker.AddStatic(trigger);
            return s => {
                tracker.BeginDynamic();
                try { OnRun(s); } finally { tracker.EndDynamic(); }
            };
        }
        protected abstract void OnRun(EpochBuilder s);
        protected void AddStaticTrigger(ITrigger trigger) => tracker.AddStatic(trigger);
        protected void AddDynamicTrigger(ITrigger trigger) => tracker.AddDynamic(trigger);
    }
    // ============================== DependencyTracker ============================================================
    internal class DependencyTracker : IDisposable {
        SpokeEngine.Friend engine;
        Action schedule;
        HashSet<ITrigger> seen = new HashSet<ITrigger>();
        List<(ITrigger t, SpokeHandle h)> staticHandles = new List<(ITrigger t, SpokeHandle h)>();
        List<(ITrigger t, SpokeHandle h)> dynamicHandles = new List<(ITrigger t, SpokeHandle h)>();
        public int depIndex;
        public DependencyTracker(SpokeEngine.Friend engine, Action schedule) {
            this.engine = engine;
            this.schedule = schedule;
        }
        public void AddStatic(ITrigger trigger) {
            if (!seen.Add(trigger)) return;
            staticHandles.Add((trigger, trigger.Subscribe(ScheduleFromTrigger(trigger, -1))));
        }
        public void BeginDynamic() {
            depIndex = 0;
            seen.Clear();
            foreach (var dep in staticHandles) seen.Add(dep.t);
        }
        public void AddDynamic(ITrigger trigger) {
            if (!seen.Add(trigger)) return;
            if (depIndex >= dynamicHandles.Count) dynamicHandles.Add((trigger, trigger.Subscribe(ScheduleFromTrigger(trigger, depIndex))));
            else if (dynamicHandles[depIndex].t != trigger) {
                dynamicHandles[depIndex].h.Dispose();
                dynamicHandles[depIndex] = (trigger, trigger.Subscribe(ScheduleFromTrigger(trigger, depIndex)));
            }
            depIndex++;
        }
        public void EndDynamic() {
            while (dynamicHandles.Count > depIndex) {
                dynamicHandles[dynamicHandles.Count - 1].h.Dispose();
                dynamicHandles.RemoveAt(dynamicHandles.Count - 1);
            }
        }
        public void Dispose() {
            seen.Clear();
            foreach (var handle in staticHandles) handle.h.Dispose();
            foreach (var handle in dynamicHandles) handle.h.Dispose();
            staticHandles.Clear(); dynamicHandles.Clear();
        }
        Action ScheduleFromTrigger(ITrigger trigger, int index) => () => {
            if (index >= depIndex) return;
            engine.FastHold();
            schedule();
            (trigger as IDeferredTrigger).OnAfterNotify(() => engine.FastRelease());
        };
    }
}
