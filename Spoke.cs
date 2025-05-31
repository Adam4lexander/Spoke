// Spoke.cs
// -----------------------------
// > Trigger
// > State
// > BaseEffect
// > Effect
// > Reaction
// > Phase
// > Memo
// > Dock
// > Node
// > SpokeEngine
// > SpokeLogger
// > KahnTopoSorter

using System;
using System.Collections.Generic;
using System.Text;

namespace Spoke {

    public delegate void EffectBlock(EffectBuilder s);
    public delegate void EffectBlock<T>(EffectBuilder s, T param);

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
    public struct SpokePool<T> where T : new() {
        Stack<T> pool; Action<T> reset;
        public static SpokePool<T> Create(Action<T> reset = null) => new SpokePool<T> { pool = new Stack<T>(), reset = reset };
        public T Get() => pool.Count > 0 ? pool.Pop() : new T();
        public void Return(T o) { reset?.Invoke(o); pool.Push(o); }
    }
    // ============================== State ============================================================
    public interface ISignal<T> : ITrigger<T> {
        T Now { get; }
    }
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
        T Use<T>(T disposable) where T : IDisposable;
        void UseSubscribe(ITrigger trigger, Action action);
        void UseSubscribe<T>(ITrigger<T> trigger, Action<T> action);
        ISignal<T> UseMemo<T>(Func<MemoBuilder, T> selector, params ITrigger[] triggers);
        ISignal<T> UseMemo<T>(string name, Func<MemoBuilder, T> selector, params ITrigger[] triggers);
        void UseEffect(EffectBlock func, params ITrigger[] triggers);
        void UseEffect(string name, EffectBlock func, params ITrigger[] triggers);
        void UseReaction(EffectBlock action, params ITrigger[] triggers);
        void UseReaction(string name, EffectBlock action, params ITrigger[] triggers);
        void UsePhase(ISignal<bool> mountWhen, EffectBlock func, params ITrigger[] triggers);
        void UsePhase(string name, ISignal<bool> mountWhen, EffectBlock func, params ITrigger[] triggers);
        IDock UseDock();
        IDock UseDock(string name);
        void OnCleanup(Action cleanup);
    }
    public abstract class BaseEffect : SpokeEngine.Computation {
        EffectBuilderImpl builder;
        public BaseEffect(string name, SpokeEngine engine, params ITrigger[] triggers) : base(name, engine, triggers) {
            builder = new EffectBuilderImpl(this);
        }
        protected void Mount(EffectBlock block) => builder.Mount(block);
        class EffectBuilderImpl : EffectBuilder {
            bool isSealed, isMounted;
            BaseEffect owner;
            public EffectBuilderImpl(BaseEffect owner) {
                this.owner = owner;
                isSealed = true;
            }
            public void Mount(EffectBlock block) {
                if (isMounted) Unmount();
                isSealed = false;
                try { block?.Invoke(this); } finally { isSealed = isMounted = true; }
            }
            public void Unmount() {
                if (!isMounted) return;
                owner.ClearChildren();
                isMounted = false;
            }
            public SpokeEngine Engine => owner.engine;
            public void Log(string msg) { NoMischief(); owner.LogFlush(msg); }
            public T D<T>(ISignal<T> signal) { NoMischief(); owner.AddDynamicTrigger(signal); return signal.Now; }
            public void Use(SpokeHandle trigger) { NoMischief(); owner.Own(trigger); }
            public T Use<T>(T disposable) where T : IDisposable { NoMischief(); owner.Own(disposable); return disposable; }
            public void UseSubscribe(ITrigger trigger, Action action) => Use(trigger != null ? trigger.Subscribe(action) : default);
            public void UseSubscribe<T>(ITrigger<T> trigger, Action<T> action) => Use(trigger != null ? trigger.Subscribe(action) : default);
            public ISignal<T> UseMemo<T>(Func<MemoBuilder, T> selector, params ITrigger[] triggers) => Use(new Memo<T>("Memo", Engine, selector, triggers));
            public ISignal<T> UseMemo<T>(string name, Func<MemoBuilder, T> selector, params ITrigger[] triggers) => Use(new Memo<T>(name, Engine, selector, triggers));
            public void UseEffect(EffectBlock buildLogic, params ITrigger[] triggers) => Use(new Effect("Effect", Engine, buildLogic, triggers));
            public void UseEffect(string name, EffectBlock buildLogic, params ITrigger[] triggers) => Use(new Effect(name, Engine, buildLogic, triggers));
            public void UseReaction(EffectBlock block, params ITrigger[] triggers) => Use(new Reaction("Reaction", Engine, block, triggers));
            public void UseReaction(string name, EffectBlock block, params ITrigger[] triggers) => Use(new Reaction(name, Engine, block, triggers));
            public void UsePhase(ISignal<bool> mountWhen, EffectBlock buildLogic, params ITrigger[] triggers) => Use(new Phase("Phase", Engine, mountWhen, buildLogic, triggers));
            public void UsePhase(string name, ISignal<bool> mountWhen, EffectBlock buildLogic, params ITrigger[] triggers) => Use(new Phase(name, Engine, mountWhen, buildLogic, triggers));
            public IDock UseDock() => Use(new Dock("Dock", Engine));
            public IDock UseDock(string name) => Use(new Dock(name, Engine));
            public void OnCleanup(Action fn) { NoMischief(); owner.OnCleanup(fn); }
            void NoMischief() { if (isSealed) throw new Exception("Cannot mutate effect: builder is sealed after mounting."); }
        }
    }
    // ============================== Effect ============================================================
    public class Effect : BaseEffect {
        EffectBlock block;
        public Effect(string name, SpokeEngine engine, EffectBlock block, params ITrigger[] triggers) : base(name, engine, triggers) {
            this.block = block;
            Schedule();
        }
        protected override void OnRun() => Mount(block);
    }
    // ============================== Reaction ============================================================
    public class Reaction : BaseEffect {
        EffectBlock block;
        public Reaction(string name, SpokeEngine engine, EffectBlock block, params ITrigger[] triggers) : base(name, engine, triggers) {
            this.block = block;
        }
        protected override void OnRun() => Mount(block);
    }
    // ============================== Phase ============================================================
    public class Phase : BaseEffect {
        EffectBlock block;
        public Phase(string name, SpokeEngine engine, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers) : base(name, engine, triggers) {
            if (mountWhen == null) return;
            AddStaticTrigger(mountWhen);
            this.block = s => { if (mountWhen.Now) block?.Invoke(s); };
            Schedule();
        }
        protected override void OnRun() => Mount(block);
    }
    // ============================== Memo ============================================================
    public class Memo<T> : SpokeEngine.Computation, ISignal<T>, IDeferredTrigger {
        State<T> state = State.Create<T>();
        public T Now => state.Now;
        Action<MemoBuilder> block;
        MemoBuilder builder;
        public Memo(string name, SpokeEngine engine, Func<MemoBuilder, T> selector, params ITrigger[] triggers) : base(name, engine, triggers) {
            builder = new MemoBuilder(new MemoBuilder.Friend { AddDynamicTrigger = AddDynamicTrigger, Own = Own });
            block = s => { if (selector != null) state.Set(selector(s)); };
            Schedule();
        }
        public SpokeHandle Subscribe(Action action) => state.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => state.Subscribe(action);
        public void Unsubscribe(Action action) => state.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => state.Unsubscribe(action);
        void IDeferredTrigger.OnAfterNotify(Action action) => (state as IDeferredTrigger).OnAfterNotify(action);
        protected override void OnRun() { ClearChildren(); block(builder); }
    }
    public class MemoBuilder { // Concrete class for IL2CPP AOT generation
        internal struct Friend {
            public Action<ITrigger> AddDynamicTrigger;
            public Func<IDisposable, IDisposable> Own;
        }
        Friend memo;
        internal MemoBuilder(Friend memo) { this.memo = memo; }
        public U D<U>(ISignal<U> signal) { memo.AddDynamicTrigger(signal); return signal.Now; }
        public U Use<U>(U disposable) where U : IDisposable { memo.Own(disposable); return disposable; }
    }
    // ============================== Dock ============================================================
    public interface IDock {
        void Use(object key, SpokeHandle handle);
        T Use<T>(object key, T disposable) where T : IDisposable;
        void Drop(object key);
        void UseEffect(object key, EffectBlock buildLogic, params ITrigger[] triggers);
        void UseEffect(string name, object key, EffectBlock buildLogic, params ITrigger[] triggers);
    }
    public class Dock : Node, IDock, IDisposable {
        Dictionary<object, SpokeHandle> handles = new Dictionary<object, SpokeHandle>();
        Dictionary<object, IDisposable> disposables = new Dictionary<object, IDisposable>();
        SpokeEngine engine;
        public Dock(string name, SpokeEngine engine) : base(name) { this.engine = engine; }
        public void Use(object key, SpokeHandle handle) {
            Drop(key);
            handles[key] = Own(handle);
        }
        public T Use<T>(object key, T disposable) where T : IDisposable {
            Drop(key);
            disposables[key] = Own(disposable);
            return disposable;
        }
        public void UseEffect(object key, EffectBlock buildLogic, params ITrigger[] triggers) => UseEffect("Effect", key, buildLogic, triggers);
        public void UseEffect(string name, object key, EffectBlock buildLogic, params ITrigger[] triggers) => Use(key, new Effect(name, engine, buildLogic, triggers));
        public void Drop(object key) {
            if (disposables.TryGetValue(key, out var child)) { Remove(child); disposables.Remove(key); }
            if (handles.TryGetValue(key, out var handle)) { Remove(handle); handles.Remove(key); }
        }
    }
    // ============================== Node ============================================================
    public struct SpokeHandle : IDisposable, IEquatable<SpokeHandle> {
        long id; Action<long> onDispose;
        public static SpokeHandle Of(long id, Action<long> onDispose) => new SpokeHandle { id = id, onDispose = onDispose };
        public void Dispose() => onDispose?.Invoke(id);
        public bool Equals(SpokeHandle other) => id == other.id && onDispose == other.onDispose;
        public override bool Equals(object obj) => obj is SpokeHandle other && Equals(other);
        public override int GetHashCode() {
            int hashCode = -1348004479;
            hashCode = hashCode * -1521134295 + id.GetHashCode();
            return hashCode * -1521134295 + EqualityComparer<Action<long>>.Default.GetHashCode(onDispose);
        }
        public static bool operator ==(SpokeHandle left, SpokeHandle right) => left.Equals(right);
        public static bool operator !=(SpokeHandle left, SpokeHandle right) => !left.Equals(right);
    }
    public abstract class Node : IDisposable, IComparable<Node> {
        static long RootCounter = 0;
        List<IDisposable> children = new List<IDisposable>();
        List<SpokeHandle> handles = new List<SpokeHandle>();
        List<Action> cleanupFuncs = new List<Action>();
        bool isChildrenDisposing;
        List<long> coords = new List<long>();
        long siblingCounter = 0;
        string name;
        public Node Owner { get; private set; }
        public Node Root => Owner != null ? Owner.Root : this;
        public ReadOnlyList<IDisposable> Children => new ReadOnlyList<IDisposable>(children);
        public Node(string name) { this.name = name; coords.Add(RootCounter++); }
        public override string ToString() => name ?? base.ToString();
        public virtual void Dispose() => ClearChildren();
        public int CompareTo(Node other) {
            var minDepth = Math.Min(coords.Count, other.coords.Count);
            for (int i = 0; i < minDepth; i++) {
                int cmp = coords[i].CompareTo(other.coords[i]);
                if (cmp != 0) return cmp;
            }
            return coords.Count.CompareTo(other.coords.Count);
        }
        protected SpokeHandle Own(SpokeHandle handle) {
            NoMischief(); handles.Add(handle); return handle;
        }
        protected T Own<T>(T child) where T : IDisposable {
            NoMischief();
            children.Add(child);
            if (child is Node node) {
                node.Owner = this;
                node.coords.Clear();
                node.coords.AddRange(coords);
                node.coords.Add(siblingCounter++);
            }
            return child;
        }
        protected void Remove(IDisposable child) {
            NoMischief();
            var index = children.IndexOf(child);
            if (index >= 0) { child.Dispose(); children.RemoveAt(index); }
        }
        protected void Remove(SpokeHandle handle) {
            NoMischief();
            var index = handles.IndexOf(handle);
            if (index >= 0) { handle.Dispose(); handles.RemoveAt(index); }
        }
        protected void OnCleanup(Action fn) => cleanupFuncs.Add(fn);
        protected void ClearChildren() {
            isChildrenDisposing = true;
            for (int i = cleanupFuncs.Count - 1; i >= 0; i--)
                try { cleanupFuncs[i]?.Invoke(); } catch (Exception e) { SpokeError.Log($"Cleanup failed in '{this}'", e); }
            cleanupFuncs.Clear();
            foreach (var triggerChild in handles) triggerChild.Dispose();
            handles.Clear();
            for (int i = children.Count - 1; i >= 0; i--)
                try { children[i].Dispose(); } catch (Exception e) { SpokeError.Log($"Failed to dispose child of '{this}': {children[i]}", e); }
            children.Clear();
            siblingCounter = 0;
            isChildrenDisposing = false;
        }
        void NoMischief() { if (isChildrenDisposing) throw new Exception("Cannot mutate Node while it's disposing"); }
    }
    // ============================== SpokeEngine ============================================================
    public enum FlushMode { Immediate, Manual }
    public class SpokeEngine {
        public FlushMode FlushMode = FlushMode.Immediate;
        FlushLogger flushLogger = FlushLogger.Create();
        KahnTopoSorter toposorter = KahnTopoSorter.Create();
        HashSet<Computation> scheduled = new HashSet<Computation>();
        Dictionary<Computation, Action> runFuncs = new Dictionary<Computation, Action>();
        DeferredQueue deferred = DeferredQueue.Create();
        FlushBuckets flushBuckets = FlushBuckets.Create();
        List<string> pendingLogs = new List<string>();
        ISpokeLogger logger;
        Action _flush;
        public SpokeEngine(FlushMode flushMode, ISpokeLogger logger = null) {
            _flush = FlushNow;
            FlushMode = flushMode;
            this.logger = logger ?? new ConsoleSpokeLogger();
        }
        public void Batch(Action action) {
            deferred.Hold();
            try { action(); } finally { deferred.Release(); }
        }
        public void LogBatch(string msg, Action action) => Batch(() => {
            action();
            if (!deferred.IsEmpty) pendingLogs.Add(msg);
        });
        void Schedule(Computation comp) {
            scheduled.Add(comp);
            if (FlushMode == FlushMode.Immediate) Flush();
        }
        public void Flush() { if (deferred.IsEmpty) deferred.Enqueue(_flush); }
        static readonly Comparison<Node> EffectComparison = (a, b) => b.CompareTo(a);
        void FlushNow() {
            if (scheduled.Count == 0) return;
            var maxPasses = 1000; var passes = 0;
            try {
                flushLogger.OnFlushStart();
                flushBuckets.Clear();
                flushBuckets.Take(scheduled);
                while (flushBuckets.Memos.Count + flushBuckets.Effects.Count > 0) {
                    if (++passes > maxPasses) throw new Exception("Exceed iteration limit - possible infinite loop");
                    while (flushBuckets.Memos.Count > 0) {
                        var sortedMemos = toposorter.Sort(flushBuckets.Memos);
                        foreach (var comp in sortedMemos) {
                            flushBuckets.Memos.Remove(comp);
                            FlushComputation(comp);
                            var memoCount = flushBuckets.Memos.Count;
                            flushBuckets.Take(scheduled);
                            if (flushBuckets.Memos.Count > memoCount) break;
                        }
                    }
                    flushBuckets.Effects.Sort(EffectComparison); // Reverse-order, to pop items from end of list
                    while (flushBuckets.Memos.Count == 0 && flushBuckets.Effects.Count > 0) {
                        var comp = flushBuckets.Effects[flushBuckets.Effects.Count - 1];
                        flushBuckets.Effects.RemoveAt(flushBuckets.Effects.Count - 1);
                        FlushComputation(comp);
                        if (scheduled.Count > 0) { flushBuckets.Take(scheduled); break; }
                    }
                }
                if (pendingLogs.Count > 0 || flushLogger.HasErrors) flushLogger.LogFlush(logger, string.Join(",", pendingLogs));
            } catch (Exception ex) {
                SpokeError.Log("Internal Flush Error: ", ex);
            } finally {
                scheduled.Clear();
                pendingLogs.Clear();
            }
        }
        void FlushComputation(Computation comp) {
            if (runFuncs.TryGetValue(comp, out var run)) run?.Invoke();
            flushLogger.OnFlushComputation(comp);
        }
        struct FlushBuckets {
            public HashSet<Computation> Memos { get; private set; }
            public List<Computation> Effects { get; private set; }
            public static FlushBuckets Create() => new FlushBuckets { Memos = new HashSet<Computation>(), Effects = new List<Computation>() };
            public void Clear() { Memos.Clear(); Effects.Clear(); }
            public void Take(HashSet<Computation> scheduled) {
                foreach (var comp in scheduled)
                    if (comp is BaseEffect) Effects.Add(comp);
                    else Memos.Add(comp);
                scheduled.Clear();
            }
        }
        public abstract class Computation : Node {
            protected SpokeEngine engine;
            DependencyTracker tracker;
            bool isPending;
            public Exception Fault { get; private set; }
            public ReadOnlyList<Computation> Dependencies => new ReadOnlyList<Computation>(tracker.dependencies);
            public Computation(string name, SpokeEngine engine, IEnumerable<ITrigger> triggers) : base(name) {
                this.engine = engine;
                engine.runFuncs[this] = Run;
                tracker = DependencyTracker.Create(this);
                foreach (var trigger in triggers) tracker.AddStatic(trigger);
                tracker.SyncDependencies();
            }
            public override void Dispose() {
                engine.runFuncs.Remove(this);
                tracker.Dispose();
                base.Dispose();
            }
            void Run() {
                if (!isPending || (Fault != null)) return;
                isPending = false; // Set now in case I trigger myself
                tracker.BeginDynamic();
                try { OnRun(); } catch (Exception ex) { Fault = ex; } finally { tracker.EndDynamic(); }
            }
            protected void AddStaticTrigger(ITrigger trigger) { tracker.AddStatic(trigger); tracker.SyncDependencies(); }
            protected void AddDynamicTrigger(ITrigger trigger) => tracker.AddDynamic(trigger);
            protected abstract void OnRun();
            protected void LogFlush(string msg) => engine.pendingLogs.Add(msg);
            Action ScheduleFromTrigger(ITrigger trigger, int index) => () => {
                if (index >= tracker.depIndex) return;
                engine.deferred.Hold();
                Schedule();
                (trigger as IDeferredTrigger).OnAfterNotify(() => engine.deferred.Release());
            };
            protected void Schedule() {
                if (isPending) return;
                isPending = true;
                engine.Schedule(this);
            }
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
    internal struct DeferredQueue {
        int holdCount; Queue<Action> queue;
        public bool IsDraining { get; private set; }
        public bool IsEmpty => queue.Count == 0 && !IsDraining;
        public static DeferredQueue Create() => new DeferredQueue { queue = new Queue<Action>() };
        public void Hold() => holdCount++;
        public void Release() {
            if (holdCount <= 0) throw new InvalidOperationException("Mismatched Release() without Hold()");
            if ((--holdCount) == 0 && !IsDraining) Drain();
        }
        public void Enqueue(Action action) {
            queue.Enqueue(action);
            if (holdCount == 0 && !IsDraining) Drain();
        }
        void Drain() {
            IsDraining = true;
            while (queue.Count > 0) queue.Dequeue()();
            IsDraining = false;
        }
    }
    public readonly struct ReadOnlyList<T> {
        readonly List<T> list;
        public ReadOnlyList(List<T> list) { this.list = list; }
        public List<T>.Enumerator GetEnumerator() => list.GetEnumerator();
        public int Count => list.Count;
        public T this[int index] => list[index];
    }
    // ============================== SpokeLogger ============================================================
    public interface ISpokeLogger {
        void Log(string message);
        void Error(string message);
    }
    public class ConsoleSpokeLogger : ISpokeLogger {
        public void Log(string msg) => Console.WriteLine(msg);
        public void Error(string msg) => Console.WriteLine(msg);
    }
    public static class SpokeError {
        internal static Action<string, Exception> Log = (msg, ex) => Console.WriteLine($"[Spoke] {msg}\n{ex}");
    }
    public struct FlushLogger {
        StringBuilder sb;
        List<SpokeEngine.Computation> runHistory;
        HashSet<Node> roots;
        public static FlushLogger Create() => new FlushLogger {
            sb = new StringBuilder(),
            runHistory = new List<SpokeEngine.Computation>(),
            roots = new HashSet<Node>()
        };
        public void OnFlushStart() { sb.Clear(); roots.Clear(); runHistory.Clear(); HasErrors = false; }
        public void OnFlushComputation(SpokeEngine.Computation c) { runHistory.Add(c); HasErrors |= c.Fault != null; }
        public bool HasErrors { get; private set; }
        public void LogFlush(ISpokeLogger logger, string msg) {
            sb.AppendLine($"[{(HasErrors ? "FLUSH ERROR" : "FLUSH")}]");
            foreach (var line in msg.Split(',')) sb.AppendLine($"-> {line}");
            foreach (var c in runHistory) roots.Add(c.Root);
            foreach (var root in roots) PrintRoot(root);
            if (HasErrors) { PrintErrors(); logger?.Error(sb.ToString()); } else logger?.Log(sb.ToString());
        }
        void PrintErrors() {
            foreach (var c in runHistory)
                if (c.Fault != null) sb.AppendLine($"\n\n--- {NodeLabel(c)} ---\n{c.Fault}");
        }
        void PrintRoot(Node root) {
            var that = this;
            sb.AppendLine();
            Traverse(0, root, (depth, x) => {
                var runIndex = that.runHistory.IndexOf(x as SpokeEngine.Computation);
                for (int i = 0; i < depth; i++) that.sb.Append("    ");
                that.sb.Append($"{that.NodeLabel(x)} {that.FaultStatus(x)}\n");
            });
        }
        string NodeLabel(IDisposable node) {
            if (node is SpokeEngine.Computation comp) {
                var indexes = new List<int>();
                for (int i = 0; i < runHistory.Count; i++)
                    if (ReferenceEquals(runHistory[i], comp)) indexes.Add(i);
                var indexStr = indexes.Count > 0 ? $"({string.Join(",", indexes)})-" : "";
                return $"|--{indexStr}{node} ";
            }
            return $"|--{node} ";
        }
        string FaultStatus(IDisposable node) {
            if (node is SpokeEngine.Computation comp && comp.Fault != null)
                if (runHistory.Contains(comp)) return $"[Faulted: {comp.Fault.GetType().Name}]";
                else return "[Faulted]";
            return "";
        }
        void Traverse(int depth, IDisposable obj, Action<int, IDisposable> action) {
            action?.Invoke(depth, obj);
            if (obj is Node node)
                foreach (var child in node.Children)
                    Traverse(depth + 1, child, action);
        }
    }
    // ============================== KahnTopoSorter ============================================================
    internal struct KahnTopoSorter {
        List<SpokeEngine.Computation> sorted;
        Dictionary<SpokeEngine.Computation, int> inDegree;
        Dictionary<SpokeEngine.Computation, List<SpokeEngine.Computation>> dependentsMap;
        Queue<SpokeEngine.Computation> queue;
        SpokePool<List<SpokeEngine.Computation>> listPool;
        public static KahnTopoSorter Create() => new KahnTopoSorter {
            sorted = new List<SpokeEngine.Computation>(),
            inDegree = new Dictionary<SpokeEngine.Computation, int>(),
            dependentsMap = new Dictionary<SpokeEngine.Computation, List<SpokeEngine.Computation>>(),
            queue = new Queue<SpokeEngine.Computation>(),
            listPool = SpokePool<List<SpokeEngine.Computation>>.Create(l => l.Clear())
        };
        public List<SpokeEngine.Computation> Sort(HashSet<SpokeEngine.Computation> dirty) {
            sorted.Clear();
            foreach (var comp in dirty) {
                var count = 0;
                foreach (var dep in comp.Dependencies) {
                    if (dirty.Contains(dep)) {
                        count++;
                        if (!dependentsMap.TryGetValue(dep, out var deps))
                            dependentsMap[dep] = deps = listPool.Get();
                        deps.Add(comp);
                    }
                }
                inDegree[comp] = count;
            }
            foreach (var comp in dirty) if (inDegree[comp] == 0) queue.Enqueue(comp);
            while (queue.Count > 0) {
                var comp = queue.Dequeue();
                sorted.Add(comp);
                if (!dependentsMap.TryGetValue(comp, out var dependents)) continue;
                foreach (var dep in dependents) if (--inDegree[dep] == 0) queue.Enqueue(dep);
            }
            Reset();
            if (sorted.Count != dirty.Count) throw new Exception("Cycle detected in computation graph!");
            return sorted;
        }
        void Reset() {
            foreach (var pair in dependentsMap) listPool.Return(pair.Value);
            inDegree.Clear();
            dependentsMap.Clear();
            queue.Clear();
        }
    }
}