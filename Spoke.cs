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
// > SpokeEngine
// > SpokeLogger
// > KahnTopoSorter

using System;
using System.Collections.Generic;
using System.Text;

namespace Spoke {

    public delegate void EffectBlock(EffectBuilder s);

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
        T Component<T>(T identity) where T : Facet;
        public bool TryGetAmbient<T>(out T context) where T : Facet;
        void OnCleanup(Action cleanup);
    }
    public static partial class EffectBuilderExtensions {
        public static void UseSubscribe(this EffectBuilder s, ITrigger trigger, Action action) => s.Use(trigger != null ? trigger.Subscribe(action) : default);
        public static void UseSubscribe<T>(this EffectBuilder s, ITrigger<T> trigger, Action<T> action) => s.Use(trigger != null ? trigger.Subscribe(action) : default);
        public static ISignal<T> UseMemo<T>(this EffectBuilder s, Func<MemoBuilder, T> selector, params ITrigger[] triggers) => s.Component(new Memo<T>("Memo", selector, triggers));
        public static ISignal<T> UseMemo<T>(this EffectBuilder s, string name, Func<MemoBuilder, T> selector, params ITrigger[] triggers) => s.Component(new Memo<T>(name, selector, triggers));
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
        KahnTopoSorter toposorter = KahnTopoSorter.Create();
        HashSet<Computation> scheduled = new HashSet<Computation>();
        DeferredQueue deferred = DeferredQueue.Create();
        FlushBuckets flushBuckets = FlushBuckets.Create();
        List<string> pendingLogs = new List<string>();
        ISpokeLogger logger;
        Action _flush; Action<long> _releaseEffect;
        long currId;
        public SpokeEngine(FlushMode flushMode, ISpokeLogger logger = null) {
            _flush = FlushNow;
            _releaseEffect = ReleaseEffect;
            FlushMode = flushMode;
            logger = logger ?? new ConsoleSpokeLogger();
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
        protected override void Schedule(Node node) {
            if (node.TryGetIdentity<Computation>(out var comp)) {
                scheduled.Add(comp);
                if (FlushMode == FlushMode.Immediate) Flush();
            } else {
                Execute(node);
            }
        }
        public void Flush() { if (deferred.IsEmpty) deferred.Enqueue(_flush); }
        static readonly Comparison<Computation> EffectComparison = (a, b) => b.CompareTo(a);
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
                            Execute(comp);
                            var memoCount = flushBuckets.Memos.Count;
                            flushBuckets.Take(scheduled);
                            if (flushBuckets.Memos.Count > memoCount) break;
                        }
                    }
                    flushBuckets.Effects.Sort(EffectComparison); // Reverse-order, to pop items from end of list
                    while (flushBuckets.Memos.Count == 0 && flushBuckets.Effects.Count > 0) {
                        var comp = flushBuckets.Effects[flushBuckets.Effects.Count - 1];
                        flushBuckets.Effects.RemoveAt(flushBuckets.Effects.Count - 1);
                        Execute(comp);
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
                    cleanup(() => {
                        tracker.Dispose();
                    });
                });
                OnMounted(s => {
                    tracker.BeginDynamic();
                    try { OnRun(s); } finally { tracker.EndDynamic(); engine.flushLogger.OnFlushComputation(this); }
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
            foreach (var c in runHistory) roots.Add((c as Facet.IFacetFriend).GetNode().Root);
            foreach (var root in roots) PrintRoot(root);
            if (HasErrors) { PrintErrors(); logger?.Error(sb.ToString()); } else logger?.Log(sb.ToString());
        }
        void PrintErrors() {
            foreach (var c in runHistory)
                if (c.Fault != null) sb.AppendLine($"\n\n--- {NodeLabel((c as Facet.IFacetFriend).GetNode())} ---\n{c.Fault}");
        }
        void PrintRoot(Node root) {
            var that = this;
            sb.AppendLine();
            Traverse(0, root, (depth, x) => {
                x.TryGetIdentity<SpokeEngine.Computation>(out var comp);
                var runIndex = that.runHistory.IndexOf(comp);
                for (int i = 0; i < depth; i++) that.sb.Append("    ");
                that.sb.Append($"{that.NodeLabel(x)} {that.FaultStatus(x)}\n");
            });
        }
        string NodeLabel(Node node) {
            if (node.TryGetIdentity<SpokeEngine.Computation>(out var comp)) {
                var indexes = new List<int>();
                for (int i = 0; i < runHistory.Count; i++)
                    if (ReferenceEquals(runHistory[i], comp)) indexes.Add(i);
                var indexStr = indexes.Count > 0 ? $"({string.Join(",", indexes)})-" : "";
                return $"|--{indexStr}{node} ";
            }
            return $"|--{node} ";
        }
        string FaultStatus(Node node) {
            if (node.TryGetIdentity<SpokeEngine.Computation>(out var comp) && comp.Fault != null)
                if (runHistory.Contains(comp)) return $"[Faulted: {comp.Fault.GetType().Name}]";
                else return "[Faulted]";
            return "";
        }
        void Traverse(int depth, Node node, Action<int, Node> action) {
            action?.Invoke(depth, node);
            foreach (var child in node.Children) Traverse(depth + 1, child, action);
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