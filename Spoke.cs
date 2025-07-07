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
        T Component<T>(Builder<T> builder) where T : Facet;
        public bool TryGetContext<T>(out T context) where T : Facet;
        void OnCleanup(Action cleanup);
    }
    public static partial class EffectBuilderExtensions {
        public static void UseSubscribe(this EffectBuilder s, ITrigger trigger, Action action) => s.Use(trigger != null ? trigger.Subscribe(action) : default);
        public static void UseSubscribe<T>(this EffectBuilder s, ITrigger<T> trigger, Action<T> action) => s.Use(trigger != null ? trigger.Subscribe(action) : default);
        public static ISignal<T> UseMemo<T>(this EffectBuilder s, Func<MemoBuilder, T> selector, params ITrigger[] triggers) => s.Component(Memo<T>.Builder("Memo", selector, triggers));
        public static ISignal<T> UseMemo<T>(this EffectBuilder s, string name, Func<MemoBuilder, T> selector, params ITrigger[] triggers) => s.Component(Memo<T>.Builder(name, selector, triggers));
        public static void UseEffect(this EffectBuilder s, EffectBlock buildLogic, params ITrigger[] triggers) => s.Component(Effect.Builder("Effect", buildLogic, triggers));
        public static void UseEffect(this EffectBuilder s, string name, EffectBlock buildLogic, params ITrigger[] triggers) => s.Component(Effect.Builder(name, buildLogic, triggers));
        public static void UseReaction(this EffectBuilder s, EffectBlock block, params ITrigger[] triggers) => s.Component(Reaction.Builder("Reaction", block, triggers));
        public static void UseReaction(this EffectBuilder s, string name, EffectBlock block, params ITrigger[] triggers) => s.Component(Reaction.Builder(name, block, triggers));
        public static void UsePhase(this EffectBuilder s, ISignal<bool> mountWhen, EffectBlock buildLogic, params ITrigger[] triggers) => s.Component(Phase.Builder("Phase", mountWhen, buildLogic, triggers));
        public static void UsePhase(this EffectBuilder s, string name, ISignal<bool> mountWhen, EffectBlock buildLogic, params ITrigger[] triggers) => s.Component(Phase.Builder(name, mountWhen, buildLogic, triggers));
        public static IDock UseDock(this EffectBuilder s) => s.Component(Dock.Builder("Dock"));
        public static IDock UseDock(this EffectBuilder s, string name) => s.Component(Dock.Builder(name));
    }
    public abstract class BaseEffect : SpokeEngine.Computation {
        EffectBuilderImpl builder;
        protected override void Build(string name, bool scheduleOnAttach, IEnumerable<ITrigger> triggers) {
            builder = new EffectBuilderImpl(this);
            base.Build(name, scheduleOnAttach, triggers);
        }
        protected void Mount(EffectBlock block) => builder.Mount(block);
        class EffectBuilderImpl : EffectBuilder, IHasCoords {
            bool isMounted;
            BaseEffect effect;
            public EffectBuilderImpl(BaseEffect owner) {
                this.effect = owner;
            }
            public void Mount(EffectBlock block) {
                if (isMounted) effect.Owner.ClearChildren();
                try { block?.Invoke(this); } finally { isMounted = true; effect.Owner.Seal(); }
            }
            TreeCoords IHasCoords.GetCoords() => effect.Owner.Coords;
            public SpokeEngine Engine => effect.engine;
            public void Log(string msg) => effect.LogFlush(msg);
            public T D<T>(ISignal<T> signal) { effect.AddDynamicTrigger(signal); return signal.Now; }
            public void Use(SpokeHandle trigger) => effect.Owner.Use(trigger);
            public T Use<T>(T disposable) where T : IDisposable { effect.Owner.Use(disposable); return disposable; }
            public T Component<T>(Builder<T> builder) where T : Facet => effect.Owner.Component(builder);
            public bool TryGetContext<T>(out T context) where T : Facet => effect.Owner.TryGetContext(out context);
            public void OnCleanup(Action fn) => effect.Owner.OnCleanup(fn);
        }
    }
    // ============================== Effect ============================================================
    public class Effect : BaseEffect {
        EffectBlock block;
        public static Builder<Effect> Builder(string name, EffectBlock block, params ITrigger[] triggers) => new(that => {
            that.block = block;
            that.Build(name, true, triggers);
        });
        protected override void OnRun() => Mount(block);
    }
    // ============================== Reaction ============================================================
    public class Reaction : BaseEffect {
        EffectBlock block;
        public static Builder<Reaction> Builder(string name, EffectBlock block, params ITrigger[] triggers) => new(that => {
            that.block = block;
            that.Build(name, false, triggers);
        });
        protected override void OnRun() => Mount(block);
    }
    // ============================== Phase ============================================================
    public class Phase : BaseEffect {
        EffectBlock block;
        public static Builder<Phase> Builder(string name, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers) => new(that => {
            if (mountWhen != null) {
                that.AddStaticTrigger(mountWhen);
                that.block = s => { if (mountWhen.Now) block?.Invoke(s); };
            }
            that.Build(name, true, triggers);
        });
        protected override void OnRun() => Mount(block);
    }
    // ============================== Memo ============================================================
    public class Memo<T> : SpokeEngine.Computation, ISignal<T>, IDeferredTrigger {
        State<T> state = State.Create<T>();
        public T Now => state.Now;
        Action<MemoBuilder> block;
        MemoBuilder builder;
        public static Builder<Memo<T>> Builder(string name, Func<MemoBuilder, T> selector, params ITrigger[] triggers) => new(that => {
            that.builder = new MemoBuilder(new MemoBuilder.Friend { AddDynamicTrigger = that.AddDynamicTrigger, Own = that.Owner.Use });
            that.block = s => { if (selector != null) that.state.Set(selector(s)); };
            that.Build(name, true, triggers);
        });
        public SpokeHandle Subscribe(Action action) => state.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => state.Subscribe(action);
        public void Unsubscribe(Action action) => state.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => state.Unsubscribe(action);
        void IDeferredTrigger.OnAfterNotify(Action action) => (state as IDeferredTrigger).OnAfterNotify(action);
        protected override void OnRun() { Owner.ClearChildren(); block(builder); }
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
    public class Dock : Facet, IDock, IHasCoords {
        string name;
        public override string ToString() => name ?? base.ToString();
        public static Builder<Dock> Builder(string name) => new(that => {
            that.name = name;
        });
        public void Use(object key, SpokeHandle handle) => Owner.Use(key, handle);
        public T Use<T>(object key, T disposable) where T : IDisposable => Owner.Use(key, disposable);
        public void UseEffect(object key, EffectBlock buildLogic, params ITrigger[] triggers) => UseEffect("Effect", key, buildLogic, triggers);
        public void UseEffect(string name, object key, EffectBlock buildLogic, params ITrigger[] triggers) => Owner.Component(key, Effect.Builder(name, buildLogic, triggers));
        public void Drop(object key) => Owner.Drop(key);
        TreeCoords IHasCoords.GetCoords() => Owner.Coords;
        protected override void OnDisposed() { }
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
    internal interface IHasCoords { TreeCoords GetCoords(); }
    public interface NodeMutator {
        TreeCoords Coords { get; }
        void Seal();
        SpokeHandle Use(SpokeHandle handle);
        SpokeHandle Use(object key, SpokeHandle handle);
        T Use<T>(T child) where T : IDisposable;
        T Use<T>(object key, T child) where T : IDisposable;
        T Component<T>(Builder<T> builder) where T : Facet;
        T Component<T>(object key, Builder<T> builder) where T : Facet;
        void Drop(object key);
        void OnCleanup(Action fn);
        bool TryGetContext<T>(out T context) where T : Facet;
        bool TryGetComponent<T>(out T component) where T : Facet;
        void ClearChildren();
    }
    public abstract class Facet : Facet.IFacetFriend {
        internal interface IFacetFriend {
            Node GetNode();
            void SetOwner(NodeMutator nodeMut, Node nodeAct);
            void Dispose();
        }
        protected NodeMutator Owner { get; private set; }
        Node nodeActual;
        Node IFacetFriend.GetNode() => nodeActual;
        void IFacetFriend.SetOwner(NodeMutator nodeMutable, Node nodeActual) {
            Owner = nodeMutable;
            this.nodeActual = nodeActual;
        }
        void IFacetFriend.Dispose() {
            OnDisposed();
            Owner = null;
        }
        protected abstract void OnDisposed();
    }
    public struct Builder<T> {
        Action<T> con;
        public Builder(Action<T> constructor = default) { con = constructor; }
        internal T Build(Action<T> bindAction) {
            var inst = Activator.CreateInstance<T>();
            bindAction?.Invoke(inst);
            try { con?.Invoke(inst); } catch (Exception e) { SpokeError.Log("Error building object", e); }
            return inst;
        }
    }
    public class Node<T> : Node where T : Facet {
        public T Identity { get; private set; }
        protected override Facet UntypedIdentity => Identity;
        Builder<T> builder;
        public Node(Builder<T> builder) : base() {
            this.builder = builder;
        }
        protected override void OnAttached() {
            builder.Build(that => {
                Identity = that;
                (that as Facet.IFacetFriend).SetOwner(Mutator, this);
            });
        }
    }
    public struct TreeCoords : IComparable<TreeCoords> {
        List<long> coords;
        public TreeCoords Extend(long idx) {
            var next = new TreeCoords { coords = new List<long>() };
            if (coords != null) next.coords.AddRange(coords);
            next.coords.Add(idx);
            return next;
        }
        public int CompareTo(TreeCoords other) {
            var myDepth = coords?.Count ?? 0;
            var otherDepth = other.coords?.Count ?? 0;
            var minDepth = Math.Min(myDepth, otherDepth);
            for (int i = 0; i < minDepth; i++) {
                int cmp = coords[i].CompareTo(other.coords[i]);
                if (cmp != 0) return cmp;
            }
            return myDepth.CompareTo(otherDepth);
        }
    }
    public abstract class Node : IDisposable {
        public static Node<T> CreateRoot<T>(Builder<T> builder) where T : Facet {
            var node = new Node<T>(builder);
            node.OnAttached();
            return node;
        }
        List<IDisposable> children = new List<IDisposable>();
        List<SpokeHandle> handles = new List<SpokeHandle>();
        Dictionary<object, IDisposable> dynamicChildren = new Dictionary<object, IDisposable>();
        Dictionary<object, SpokeHandle> dynamicHandles = new Dictionary<object, SpokeHandle>();
        TreeCoords coords;
        MutatorImpl mutator;
        long siblingCounter = 0;
        protected abstract Facet UntypedIdentity { get; }
        protected NodeMutator Mutator => mutator;
        public Node Owner { get; private set; }
        public Node Root => Owner != null ? Owner.Root : this;
        public ReadOnlyList<IDisposable> Children => new ReadOnlyList<IDisposable>(children);
        protected Node() { 
            mutator = new MutatorImpl(this);
        }
        public bool TryGetIdentity<T>(out T identity) where T : Facet => (identity = (T)UntypedIdentity) != null;
        public override string ToString() => UntypedIdentity.ToString();
        public virtual void Dispose() {
            mutator.ClearChildren();
            (UntypedIdentity as Facet.IFacetFriend).Dispose();
        }
        void UsedBy(Node parent) {
            if (Owner != null) throw new Exception($"Node {this} was used by {parent}, but it's already attached to {Owner}");
            Owner = parent;
            coords = parent.coords.Extend(parent.siblingCounter++);
            OnAttached();
        }
        protected abstract void OnAttached();
        class MutatorImpl : NodeMutator {
            Node node;
            List<Action> cleanupFuncs = new List<Action>();
            bool isChildrenDisposing, isSealed;
            public MutatorImpl(Node node) {
                this.node = node;
            }
            public TreeCoords Coords => node.coords;
            public void Seal() => isSealed = true;
            public SpokeHandle Use(SpokeHandle handle) {
                NoMischief(); node.handles.Add(handle); return handle;
            }
            public SpokeHandle Use(object key, SpokeHandle handle) {
                Drop(key);
                return node.dynamicHandles[key] = Use(handle);
            }
            public T Use<T>(T child) where T : IDisposable {
                NoMischief();
                node.children.Add(child);
                if (child is Node cn) cn.UsedBy(node);
                return child;
            }
            public T Use<T>(object key, T child) where T : IDisposable {
                Drop(key);
                return (T)(node.dynamicChildren[key] = Use(child));
            }
            public T Component<T>(Builder<T> builder) where T : Facet {
                NoMischief();
                return Use(new Node<T>(builder)).Identity;
            }
            public T Component<T>(object key, Builder<T> builder) where T : Facet {
                Drop(key);
                return Use(key, new Node<T>(builder)).Identity;
            }
            public void Drop(object key) {
                NoMischief();
                if (node.dynamicChildren.TryGetValue(key, out var child)) {
                    var index = node.children.IndexOf(child);
                    if (index >= 0) { child.Dispose(); node.children.RemoveAt(index); }
                    node.dynamicChildren.Remove(key);
                }
                if (node.dynamicHandles.TryGetValue(key, out var handle)) {
                    var index = node.handles.IndexOf(handle);
                    if (index >= 0) { handle.Dispose(); node.handles.RemoveAt(index); }
                    node.dynamicHandles.Remove(key);
                }
            }
            public void OnCleanup(Action fn) { NoMischief(); cleanupFuncs.Add(fn); }
            public bool TryGetContext<T>(out T context) where T : Facet {
                context = default(T);
                for (var curr = node.Owner; curr != null; curr = curr.Owner)
                    if (curr.TryGetIdentity<T>(out var o)) { context = o; return true; }
                return false;
            }
            public bool TryGetComponent<T>(out T component) where T : Facet {
                if (node.TryGetIdentity(out component)) return true;
                foreach (var child in node.Children) {
                    if (!(child is Node n)) continue;
                    if (n.Mutator.TryGetComponent(out component)) return true;
                }
                return false;
            }
            public void ClearChildren() {
                isChildrenDisposing = true;
                for (int i = cleanupFuncs.Count - 1; i >= 0; i--)
                    try { cleanupFuncs[i]?.Invoke(); } catch (Exception e) { SpokeError.Log($"Cleanup failed in '{this}'", e); }
                cleanupFuncs.Clear();
                foreach (var triggerChild in node.handles) triggerChild.Dispose();
                node.handles.Clear();
                for (int i = node.children.Count - 1; i >= 0; i--)
                    try { node.children[i].Dispose(); } catch (Exception e) { SpokeError.Log($"Failed to dispose child of '{this}': {node.children[i]}", e); }
                node.children.Clear();
                node.siblingCounter = 0;
                node.dynamicChildren.Clear();
                node.dynamicHandles.Clear();
                isSealed = false;
                isChildrenDisposing = false;
            }
            void NoMischief() {
                if (isChildrenDisposing) throw new Exception("Cannot mutate Node while it's disposing");
                if (isSealed) throw new Exception("Cannot mutate Node after it's sealed");
            }
        }
    }
    // ============================== SpokeEngine ============================================================
    public enum FlushMode { Immediate, Manual }
    public class SpokeEngine : Facet {
        public static SpokeEngine Create(FlushMode flushMode, ISpokeLogger logger = null) => Node.CreateRoot(Builder(flushMode, logger)).Identity;
        public static Builder<SpokeEngine> Builder(FlushMode flushMode, ISpokeLogger logger = null) => new(that => {
            that._flush = that.FlushNow;
            that._releaseEffect = that.ReleaseEffect;
            that.FlushMode = flushMode;
            that.logger = logger ?? new ConsoleSpokeLogger();
        });
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
        public SpokeHandle UseEffect(string name, EffectBlock buildLogic, params ITrigger[] triggers) {
            Owner.Component(currId, Effect.Builder(name, buildLogic, triggers));
            return SpokeHandle.Of(currId++, _releaseEffect);
        }
        void ReleaseEffect(long id) => Owner.Drop(id);
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
                            (comp as IRunnable).Run();
                            var memoCount = flushBuckets.Memos.Count;
                            flushBuckets.Take(scheduled);
                            if (flushBuckets.Memos.Count > memoCount) break;
                        }
                    }
                    flushBuckets.Effects.Sort(EffectComparison); // Reverse-order, to pop items from end of list
                    while (flushBuckets.Memos.Count == 0 && flushBuckets.Effects.Count > 0) {
                        var comp = flushBuckets.Effects[flushBuckets.Effects.Count - 1];
                        flushBuckets.Effects.RemoveAt(flushBuckets.Effects.Count - 1);
                        (comp as IRunnable).Run();
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
        protected override void OnDisposed() { }
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
        interface IRunnable { void Run(); }
        public abstract class Computation : Facet, IRunnable, IComparable<Computation> {
            protected SpokeEngine engine;
            DependencyTracker tracker;
            bool isPending, isDisposed;
            StaleContext staleCtx;
            string name;
            public Exception Fault { get; private set; }
            public ReadOnlyList<Computation> Dependencies => new ReadOnlyList<Computation>(tracker.dependencies);
            public override string ToString() => name ?? base.ToString();
            public int CompareTo(Computation other) => Owner.Coords.CompareTo(other.Owner.Coords);
            protected virtual void Build(string name, bool scheduleOnAttach, IEnumerable<ITrigger> triggers) {
                this.name = name;
                tracker = DependencyTracker.Create(this);
                foreach (var trigger in triggers) tracker.AddStatic(trigger);
                tracker.SyncDependencies();
                Owner.TryGetContext(out engine);
                Owner.TryGetContext<Computation>(out var parentComp);
                staleCtx = new StaleContext(parentComp?.staleCtx);
                if (scheduleOnAttach) Schedule();
            }
            protected override void OnDisposed() {
                isDisposed = true;
                tracker.Dispose();
                staleCtx.Dispose();
            }
            void IRunnable.Run() {
                if (isDisposed || !isPending || staleCtx.IsStale || (Fault != null)) return;
                isPending = false; // Set now in case I trigger myself
                tracker.BeginDynamic();
                try { OnRun(); } catch (Exception ex) { Fault = ex; } finally { tracker.EndDynamic(); }
                engine.flushLogger.OnFlushComputation(this);
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
                if (isPending || staleCtx.IsStale) return;
                isPending = true;
                staleCtx.MarkDescendantsStale();
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
        class StaleContext : IDisposable {
            StaleContext parent;
            List<StaleContext> children = new List<StaleContext>();
            public bool IsStale { get; private set; }
            public StaleContext(StaleContext parent) {
                this.parent = parent;
                parent?.children.Add(this);
            }
            public void MarkDescendantsStale() {
                foreach (var c in children) if (!c.IsStale) { c.IsStale = true; c.MarkDescendantsStale(); }
            }
            public void Dispose() {
                children.Clear();
                if (!(parent?.IsStale) ?? false) parent.children.Remove(this);
                IsStale = false;
                parent = null;
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
        public int Count => list?.Count ?? 0;
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
            foreach (var c in runHistory) roots.Add((c as Facet.IFacetFriend).GetNode().Root);
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
        string NodeLabel(object node) {
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