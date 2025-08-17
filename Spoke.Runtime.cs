// Spoke.Runtime.cs
// -----------------------------
// > SpokeTree
// > Epoch
// > LambdaEpoch
// > Dock
// > SpokeEngine
// > ControlStack
// > SpokeException
// > OrderedWorkSet
// > TreeCoords
// > PackedTreeCoords128
// > SpokeHandle
// > SpokeLogger
// > SpokeIntrospect
// > SpokePool
// > ReadOnlyList
// > DeferredQueue

using System;
using System.Collections.Generic;

namespace Spoke {

    public delegate TickBlock InitBlock(EpochBuilder s);
    public delegate void TickBlock(EpochBuilder s);

    // ============================== SpokeTree ============================================================
    /// <summary>
    /// The SpokeTree hosts the root Engine of the tree. It lets you instantiate a tree, or dispose it.
    /// </summary>
    public interface ISpokeTree<out T> : IDisposable where T : SpokeEngine { T Root { get; } void Start(); bool IsAlive { get; } }
    public static class SpokeTree { 
        public static SpokeTree<T> Create<T>(T root) where T : SpokeEngine => new SpokeTree<T>(root);
        public static SpokeTree<T> CreateAndStart<T>(T root) where T : SpokeEngine {
            var tree = Create(root);
            try { 
                tree.Start(); 
            } catch (Exception e) {
                SpokeError.Log($"Failed to start SpokeTree: {root}, will dispose", e);
                tree.Dispose(); 
            }
            return tree;
        }
    }
    public class SpokeTree<T> : ISpokeTree<T> where T : SpokeEngine {
        public T Root { get; private set; }
        public bool IsAlive { get; private set; }
        public SpokeTree(T root) { Root = root; }
        public void Start() {
            if ((Root as Epoch.Friend).IsInit()) throw new InvalidOperationException($"Cannot start engine {Root}, it's already started");
            IsAlive = true;
            (Root as Epoch.Friend).Init(null);
        }
        public void Dispose() {
            if (!IsAlive) return;
            (Root as Epoch.Friend).Detach();
            IsAlive = false;
        }
    }
    // ============================== Epoch ============================================================
    /// <summary>
    /// A declarative, stateful execution unit that lives in the lifecycle tree.
    /// Epochs are invoked declaratively, mounted into nodes, and persist as active objects.
    /// They maintain state, respond to context, expose behaviour, and may spawn child epochs.
    /// </summary>
    public abstract class Epoch : Epoch.Friend, Epoch.Introspect {
        internal interface Friend { bool IsInit(); void Init(Epoch parent); void Tick(); void Detach(); List<object> GetExports(); }
        internal interface Introspect { List<Epoch> GetChildren(List<Epoch> storeIn = null); Epoch GetParent(); }
        readonly struct AttachRecord {
            public enum Kind : byte { Cleanup, Handle, Use, Call, Export }
            public readonly Kind Type;
            public readonly object AsObj;
            public readonly SpokeHandle Handle;
            public AttachRecord(SpokeHandle handle) { this = default; Type = Kind.Handle; Handle = handle; }
            public AttachRecord(Kind type, object asObj) { this = default; Type = type; AsObj = asObj; }
            public void Detach(Epoch that) {
                if (Type == Kind.Cleanup) try { (AsObj as Action)?.Invoke(); } catch (Exception e) { SpokeError.Log($"Cleanup failed in '{that}'", e); }
                if (Type == Kind.Handle) Handle.Dispose();
                if (Type == Kind.Use) try { (AsObj as IDisposable).Dispose(); } catch (Exception e) { SpokeError.Log($"Dispose failed in '{that}'", e); }
                if (Type == Kind.Call) try { (AsObj as Epoch).DetachFrom(0); that.siblingCounter--; } catch (Exception e) { SpokeError.Log($"Failed to cleanup child of '{that}': {AsObj}", e); }
            }
        }
        List<AttachRecord> attachEvents = new();
        long siblingCounter;
        public TreeCoords Coords { get; private set; }
        Epoch parent;
        int attachIndex, tickAttachStart = -1, detachFrom = int.MaxValue;
        bool isInit;
        SpokeEngine engine;
        TickBlock tickBlock;
        ControlStack.Handle controlHandle;
        Action _tick, _detach;
        protected string Name = null;
        public Exception Fault { get; private set; }
        public override string ToString() => Name ?? GetType().Name;
        public int CompareTo(Epoch other) => Coords.CompareTo(other.Coords);
        void DetachFrom(int i) {
            if (i < 0 || i >= attachEvents.Count) return;
            var isReentrant = detachFrom < int.MaxValue;
            detachFrom = Math.Min(detachFrom, i);
            if (isReentrant) return;
            while (attachEvents.Count > detachFrom) {
                attachEvents[attachEvents.Count - 1].Detach(this);
                attachEvents.RemoveAt(attachEvents.Count - 1);
            }
            tickAttachStart = Math.Min(tickAttachStart, attachEvents.Count);
            detachFrom = int.MaxValue;
        }
        bool Friend.IsInit() => isInit;
        void Friend.Init(Epoch parent) {
            // Attach
            isInit = true;
            _tick = (this as Friend).Tick; _detach = Detach;
            this.parent = parent;
            Coords = (parent == null || parent is SpokeEngine) ? default : parent.Coords.Extend(parent.siblingCounter++);
            attachIndex = parent != null ? parent.attachEvents.Count - 1 : -1;
            engine = (parent as SpokeEngine) ?? parent?.engine;
            // Run Init
            controlHandle = (ControlStack.Local as ControlStack.Friend).Push(new ControlStack.Frame(ControlStack.FrameKind.Init, this));
            try {
                tickBlock = Init(new EpochBuilder(new EpochMutations(this)));
                tickAttachStart = attachEvents.Count;
            } catch (Exception e) {
                if (!(e is SpokeException se)) {
                    Fault = e;
                    throw new SpokeException("Uncaught Exception in Init", ControlStack.Local, e);
                }
                if (!se.SkipMarkFaulted) Fault = se;
                se.SkipMarkFaulted = false;
                throw;
            } finally {
                (ControlStack.Local as ControlStack.Friend).Pop();
            }
            RequestTick();
        }
        void Friend.Tick() {
            controlHandle = (ControlStack.Local as ControlStack.Friend).Push(new ControlStack.Frame(ControlStack.FrameKind.Tick, this));
            DetachFrom(tickAttachStart);
            try { 
                tickBlock?.Invoke(new EpochBuilder(new EpochMutations(this))); 
            } catch (Exception e) {
                if (!(e is SpokeException se)) {
                    Fault = e;
                    throw new SpokeException("Uncaught Exception in Tick", ControlStack.Local, e);
                }
                if (!se.SkipMarkFaulted) Fault = se;
                se.SkipMarkFaulted = false;
                throw;
            } finally {
                (ControlStack.Local as ControlStack.Friend).Pop();
            }
        }
        void Friend.Detach() => controlHandle.OnPop(_detach);
        void Detach() {
            if (parent != null && attachIndex >= 0) parent.DetachFrom(attachIndex);
            else DetachFrom(0);
        }
        List<Epoch> Introspect.GetChildren(List<Epoch> storeIn) {
            storeIn = storeIn ?? new List<Epoch>();
            foreach (var evt in attachEvents) if (evt.Type == AttachRecord.Kind.Call) storeIn.Add(evt.AsObj as Epoch);
            return storeIn;
        }
        Epoch Introspect.GetParent() => parent;
        List<object> Friend.GetExports() {
            var result = new List<object>();
            var startIndex = attachEvents.Count - 1;
            for (var anc = this; anc != null; startIndex = anc.attachIndex, anc = anc.parent) {
                for (var i = startIndex; i >= 0; i--) {
                    var evt = anc.attachEvents[i];
                    if (evt.Type == AttachRecord.Kind.Export) result.Add(evt.AsObj);
                }
            }
            return result;
        }
        protected abstract TickBlock Init(EpochBuilder s);
        void RequestTick() {
            if (Fault != null) return;
            if (engine == null) controlHandle.OnPop(_tick);
            else (engine as SpokeEngine.Friend).Schedule(this);
        }
        internal struct EpochMutations {
            Epoch owner;
            public EpochMutations(Epoch owner) => this.owner = owner;
            public T Import<T>() {
                var startIndex = owner.attachEvents.Count - 1;
                for (var anc = owner; anc != null; startIndex = anc.attachIndex, anc = anc.parent) {
                    for (var i = startIndex; i >= 0; i--) {
                        var evt = anc.attachEvents[i];
                        if (evt.Type == AttachRecord.Kind.Export && evt.AsObj is T o) return o;
                    }
                }
                throw new Exception($"Failed to import: {typeof(T).Name}");
            }
            public SpokeHandle Use(SpokeHandle handle) {
                NoMischief(); owner.attachEvents.Add(new AttachRecord(handle)); return handle;
            }
            public T Use<T>(T disposable) where T : IDisposable {
                NoMischief(); owner.attachEvents.Add(new AttachRecord(AttachRecord.Kind.Use, disposable)); return disposable;
            }
            public T Call<T>(T epoch) where T : Epoch {
                NoMischief();
                if (epoch.parent != null) throw new InvalidOperationException("Tried to attach an epoch which was already attached");
                owner.attachEvents.Add(new AttachRecord(AttachRecord.Kind.Call, epoch));
                (epoch as Friend).Init(owner);
                return epoch;
            }
            public T Export<T>(T obj) {
                NoMischief();
                owner.attachEvents.Add(new AttachRecord(AttachRecord.Kind.Export, obj));
                return obj;
            }
            public void OnCleanup(Action fn) {
                NoMischief(); owner.attachEvents.Add(new AttachRecord(AttachRecord.Kind.Cleanup, fn));
            }
            public void RequestTick() => owner.RequestTick();
            void NoMischief() {
                if (!owner.controlHandle.IsActive) throw new InvalidOperationException("Tried to mutate an Epoch that's been sealed for further changes.");
            }
        }
    }
    public struct EpochBuilder {
        Epoch.EpochMutations s;
        internal EpochBuilder(Epoch.EpochMutations s) { this.s = s; }
        public SpokeHandle Use(SpokeHandle handle) => s.Use(handle);
        public T Use<T>(T disposable) where T : IDisposable => s.Use(disposable);
        public T Call<T>(T epoch) where T : Epoch => s.Call(epoch);
        public T Export<T>(T obj) => s.Export(obj);
        public T Import<T>() => s.Import<T>();
        public void OnCleanup(Action fn) => s.OnCleanup(fn);
        public void RequestTick() => s.RequestTick();
    }
    // ============================== LambdaEpoch ============================================================
    public class LambdaEpoch : Epoch {
        InitBlock block;
        public LambdaEpoch(string name, InitBlock block) { Name = name; this.block = block; }
        public LambdaEpoch(InitBlock block) : this("LambdaEpoch", block) { }
        protected override TickBlock Init(EpochBuilder s) => block(s);
    }
    // ============================== Dock ============================================================
    public class Dock : Epoch, Dock.Introspect {
        new internal interface Introspect { List<Epoch> GetChildren(List<Epoch> storeIn = null); }
        Dictionary<object, ISpokeTree<DockedEngine>> dynamicChildren = new Dictionary<object, ISpokeTree<DockedEngine>>();
        DeferredQueue deferred = new();
        bool isDetaching;
        long siblingCounter;
        List<object> scope = new();
        OrderedWorkSet<DockedEngine> pending = new((a, b) => b.CompareTo(a));
        Action _takeScheduled;
        public Dock() { Name = "Dock"; }
        public Dock(string name) { Name = name; }
        public T Call<T>(object key, T epoch) where T : Epoch {
            if (isDetaching) throw new Exception("Cannot Call while detaching");
            Drop(key);
            deferred.FastHold();
            dynamicChildren.Add(key, SpokeTree.Create(new DockedEngine(this, epoch, siblingCounter++)));
            try { dynamicChildren[key].Start(); } finally { deferred.FastRelease(); }
            return epoch;
        }
        public void Drop(object key) {
            if (!dynamicChildren.TryGetValue(key, out var child)) return;
            child.Dispose();
            dynamicChildren.Remove(key);
        }
        protected override TickBlock Init(EpochBuilder s) {
            scope = (this as Epoch.Friend).GetExports();
            _takeScheduled = () => { pending.Take(); s.RequestTick(); };
            s.OnCleanup(() => {
                isDetaching = true;
                var orderedChildren = GetOrderedChildren(new());
                for (int i = orderedChildren.Count - 1; i >= 0; i--) orderedChildren[i].Dispose();
                dynamicChildren.Clear();
            });
            return s => {
                if (!pending.Has) return;
                deferred.FastHold();
                bool hasMore = false;
                try {
                    pending.Peek().Tick();
                } catch (SpokeException se) {
                    se.SkipMarkFaulted = true;
                    throw;
                } finally {
                    if (!hasMore) pending.Pop();
                    deferred.FastRelease();
                    if (pending.Has) s.RequestTick();
                }
            };
        }
        void Schedule(DockedEngine root) {
            pending.Enqueue(root);
            deferred.Enqueue(_takeScheduled);
        }
        List<ISpokeTree<DockedEngine>> GetOrderedChildren(List<ISpokeTree<DockedEngine>> storeIn) {
            foreach (var r in dynamicChildren) storeIn.Add(r.Value);
            storeIn.Sort((a, b) => a.Root.CompareTo(b.Root));
            return storeIn;
        }
        List<Epoch> Introspect.GetChildren(List<Epoch> storeIn) {
            storeIn = storeIn ?? new List<Epoch>();
            foreach (var c in GetOrderedChildren(new())) storeIn.Add(c.Root);
            return storeIn;
        }
        class DockedEngine : SpokeEngine, IComparable<DockedEngine> {
            Dock dock; Epoch epoch; long idx; Func<bool> tickCommand;
            public DockedEngine(Dock dock, Epoch epoch, long idx) { this.dock = dock; this.epoch = epoch; this.idx = idx; }
            public int CompareTo(DockedEngine other) => idx.CompareTo(other.idx);
            public bool Tick() => tickCommand?.Invoke() ?? false;
            protected override Epoch Bootstrap(EngineBuilder s) {
                tickCommand = () => { s.RequestTick(); return s.HasPending; };
                s.OnCleanup(() => tickCommand = null);
                s.OnHasPending(() => dock.Schedule(this));
                s.OnTick(s => s.RunNext());
                return new LambdaEpoch("Portal", s => {
                    for (int i = dock.scope.Count - 1; i >= 0; i--) s.Export(dock.scope[i]);
                    s.Call(epoch);
                    return null;
                });
            }
        }
    }
    // ============================== SpokeEngine ============================================================
    public abstract class SpokeEngine : Epoch, SpokeEngine.Friend {
        new internal interface Friend { void Schedule(Epoch epoch); }
        Runtime runtime = new();
        void Friend.Schedule(Epoch epoch) => runtime.Schedule(epoch);
        protected sealed override TickBlock Init(EpochBuilder s) {
            var root = Bootstrap(new EngineBuilder(s, runtime));
            runtime.Seal();
            s.Export(this);
            s.Call(root);
            return s => runtime.TriggerTick(s);
        }
        protected abstract Epoch Bootstrap(EngineBuilder s);
        internal class Runtime {
            public Epoch Next => pending.Peek();
            public bool HasPending => Next != null;
            List<Action> onHasPending = new();
            List<Action<TickContext>> onTick = new();
            OrderedWorkSet<Epoch> pending = new((a, b) => b.CompareTo(a));
            bool isRunning, isSealed;
            public void Seal() => isSealed = true;
            public void OnHasPending(Action fn) { NoMischief(); onHasPending.Add(fn); }
            public void OnTick(Action<TickContext> fn) { NoMischief(); onTick.Add(fn); }
            public void TriggerHasPending() {
                foreach (var fn in onHasPending) try { fn?.Invoke(); } catch (Exception e) { SpokeError.Log("Error invoking OnHasPending", e); }
            }
            public void TriggerTick(EpochBuilder s) {
                isRunning = true;
                try { foreach (var fn in onTick) fn?.Invoke(new TickContext(s, this)); } finally { isRunning = false; }
            }
            public void Break() {
                if (!isRunning) throw new Exception("Break() must be called from within an OnTick block");
                if (!HasPending) return;
                (Next as Epoch.Friend).Detach();
                while (pending.Has) pending.Pop();
            }
            public Epoch RunNext() {
                if (!isRunning) throw new Exception("RunNext() must be called from within an OnTick block");
                if (!HasPending) return null;
                var ticked = pending.Pop();
                try { (ticked as Epoch.Friend).Tick(); } finally { pending.Take(); }
                return ticked;
            }
            public void Schedule(Epoch epoch) {
                if (epoch.Fault != null) return;
                pending.Enqueue(epoch);
                if (!isRunning) {
                    var prevHasPending = HasPending;
                    pending.Take();
                    if (!prevHasPending && HasPending) TriggerHasPending();
                }
            }
            void NoMischief() {
                if (isSealed) throw new InvalidOperationException("Cannot mutate engine after it's sealed");
            }
        }
    }
    public struct EngineBuilder {
        EpochBuilder s;
        SpokeEngine.Runtime r;
        internal EngineBuilder(EpochBuilder s, SpokeEngine.Runtime es) { this.s = s; this.r = es; }
        public bool HasPending => r.HasPending;
        public void Use(SpokeHandle trigger) => s.Use(trigger);
        public T Use<T>(T disposable) where T : IDisposable => s.Use(disposable);
        public T Export<T>(T obj) => s.Export(obj);
        public T Import<T>() => s.Import<T>();
        public void OnCleanup(Action fn) => s.OnCleanup(fn);
        public void OnHasPending(Action fn) => r.OnHasPending(fn);
        public void OnTick(Action<TickContext> fn) => r.OnTick(fn);
        public void RequestTick() => s.RequestTick();
    }
    public struct TickContext {
        EpochBuilder s;
        SpokeEngine.Runtime r;
        internal TickContext(EpochBuilder s, SpokeEngine.Runtime es) { this.s = s; this.r = es; }
        public bool HasPending => r.HasPending;
        public void Break() => r.Break();
        public Epoch PeekNext() => r.Next;
        public Epoch RunNext() => r.RunNext();
        public void RequestTick() => s.RequestTick();
    }
    // ============================== ControlStack ============================================================
    public class ControlStack : ControlStack.Friend {
        public static ControlStack Local { get; } = new ControlStack();
        internal interface Friend { Handle Push(Frame frame); void Pop(); }
        public enum FrameKind : byte { None, Init, Tick }
        public readonly struct Frame {
            public readonly Epoch Epoch;
            public readonly FrameKind Type;
            public Frame(FrameKind type, Epoch epoch) { Type = type; Epoch = epoch; }
        }
        internal readonly struct Handle {
            public readonly ControlStack Stack;
            public readonly int Index;
            readonly long version;
            public Frame Frame => IsActive ? Stack.frames[Index] : default;
            public bool IsActive => Stack != null && Index < Stack.frames.Count && version == Stack.versions[Index];
            public Handle(ControlStack stack, int index, long version) { Stack = stack; Index = index; this.version = version; }
            public void OnPop(Action fn) { if (!IsActive) fn?.Invoke(); else Stack.deferOnPop[Index].Add(fn); }
        }
        long versionCounter;
        SpokePool<List<Action>> fnlPool = SpokePool<List<Action>>.Create(l => l.Clear());
        List<Frame> frames = new List<Frame>();
        List<long> versions = new List<long>();
        List<List<Action>> deferOnPop = new List<List<Action>>();
        public ReadOnlyList<Frame> Frames => new ReadOnlyList<Frame>(frames);
        Handle Friend.Push(Frame frame) {
            frames.Add(frame);
            versions.Add(versionCounter++);
            deferOnPop.Add(fnlPool.Get());
            return new Handle(this, frames.Count-1, versions[versions.Count-1]);
        }
        void Friend.Pop() { 
            frames.RemoveAt(frames.Count-1); 
            versions.RemoveAt(versions.Count-1);
            var onPop = deferOnPop[deferOnPop.Count-1];
            deferOnPop.RemoveAt(deferOnPop.Count-1);
            foreach (var fn in onPop) fn?.Invoke();
            fnlPool.Return(onPop);
        }
    }
    // ============================== SpokeException ============================================================
    // TODO: Remove strong refs to Epoch instances. Take data snapshot, enough for toString() of
    // stack trace, and a weakref to the epoch.
    public sealed class SpokeException : Exception {
        List<ControlStack.Frame> stackSnapshot = new List<ControlStack.Frame>();
        public bool SkipMarkFaulted;
        public ReadOnlyList<ControlStack.Frame> StackSnapshot => new ReadOnlyList<ControlStack.Frame>();
        public SpokeException(string msg, ControlStack stack, Exception inner) : base(msg, inner) {
            foreach (var frame in stack.Frames) stackSnapshot.Add(frame);
        }
    }
    // ============================== OrderedWorkSet ============================================================
    internal class OrderedWorkSet<T> {
        List<T> incoming = new(); HashSet<T> set = new(); List<T> list = new(); Comparison<T> comp;
        public OrderedWorkSet(Comparison<T> comp) { this.comp = comp; }
        public bool Has => list.Count > 0;
        public void Enqueue(T t) { incoming.Add(t); }
        public void Take() { if (incoming.Count == 0) return; foreach (var t in incoming) if (set.Add(t)) list.Add(t); incoming.Clear(); list.Sort(comp); }
        public T Pop() { var x = list[^1]; list.RemoveAt(list.Count - 1); set.Remove(x); return x; }
        public T Peek() => list.Count > 0 ? list[^1] : default;
    }
    // ============================== TreeCoords ============================================================
    /// <summary>
    /// Determines the imperative ordering for a node in the call-tree. It's used to sort nodes by imperative
    /// execution order. This struct is the slow but robust fallback in case it doesn't fit into PackedTree128
    /// </summary>
    public struct TreeCoords : IComparable<TreeCoords> {
        List<long> coords;
        PackedTreeCoords128 packed;
        public TreeCoords Extend(long idx) {
            var next = new TreeCoords { coords = new List<long>() };
            if (coords != null) next.coords.AddRange(coords);
            next.coords.Add(idx);
            next.packed = PackedTreeCoords128.Pack(next.coords);
            return next;
        }
        public int CompareTo(TreeCoords other) {
            if (packed.IsValid && other.packed.IsValid) return packed.CompareTo(other.packed);
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
    // ============================== PackedTreeCoords128 ============================================================
    /// <summary>
    /// Efficiently encodes up to 16 tree layers, with 256 nodes per layer. For Fast array sorting.
    /// </summary>
    public readonly struct PackedTreeCoords128 : IComparable<PackedTreeCoords128> {
        readonly ulong hi; // top 8 levels
        readonly ulong lo; // bottom 8 levels
        readonly byte depth;
        public PackedTreeCoords128(ulong hi, ulong lo, byte depth) { this.hi = hi; this.lo = lo; this.depth = depth; }
        public static PackedTreeCoords128 Invalid => new PackedTreeCoords128(0, 0, byte.MaxValue);
        public bool IsValid => depth < byte.MaxValue;
        public static PackedTreeCoords128 Pack(List<long> coords) {
            if (coords == null || coords.Count > 16) return Invalid;
            ulong hi = 0, lo = 0;
            for (int i = 0; i < coords.Count; i++) {
                long val = coords[i];
                if (val < 0 || val > 255) return Invalid;
                if (i < 8) hi |= ((ulong)val << ((7 - i) * 8));
                else lo |= ((ulong)val << ((15 - i) * 8));
            }
            return new PackedTreeCoords128(hi, lo, (byte)coords.Count);
        }
        public int CompareTo(PackedTreeCoords128 other) {
            int cmp = hi.CompareTo(other.hi);
            if (cmp != 0) return cmp;
            cmp = lo.CompareTo(other.lo);
            if (cmp != 0) return cmp;
            return depth.CompareTo(other.depth);
        }
    }
    // ============================== SpokeHandle ============================================================
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
        internal static ISpokeLogger DefaultLogger = new ConsoleSpokeLogger();
    }
    // ============================== SpokeIntrospect ============================================================
    public static class SpokeIntrospect {
        static SpokePool<List<Epoch>> elPool = SpokePool<List<Epoch>>.Create(l => l.Clear());
        public static List<Epoch> GetChildren(Epoch epoch, List<Epoch> storeIn = null) {
            storeIn = storeIn ?? new List<Epoch>();
            if (epoch is Dock.Introspect d) return d.GetChildren(storeIn);
            return (epoch as Epoch.Introspect).GetChildren(storeIn);
        }
        public static Epoch GetParent(Epoch epoch) => (epoch as Epoch.Introspect).GetParent();
        public static void Traverse(Epoch epoch, Func<int, Epoch, bool> fn) => TraverseRecurs(0, epoch, fn);
        static void TraverseRecurs(int depth, Epoch epoch, Func<int, Epoch, bool> fn) {
            var shouldContinue = fn?.Invoke(depth, epoch) ?? false;
            if (!shouldContinue) return;
            var children = GetChildren(epoch, elPool.Get());
            foreach (var c in children) TraverseRecurs(depth + 1, c, fn);
            elPool.Return(children);
        }
    }
    // ============================== SpokePool ============================================================
    public struct SpokePool<T> where T : new() {
        Stack<T> pool; Action<T> reset;
        public static SpokePool<T> Create(Action<T> reset = null) => new SpokePool<T> { pool = new Stack<T>(), reset = reset };
        public T Get() => pool.Count > 0 ? pool.Pop() : new T();
        public void Return(T o) { reset?.Invoke(o); pool.Push(o); }
    }
    // ============================== ReadOnlyList ============================================================
    public readonly struct ReadOnlyList<T> {
        readonly List<T> list;
        public ReadOnlyList(List<T> list) { this.list = list; }
        public List<T>.Enumerator GetEnumerator() => list.GetEnumerator();
        public int Count => list?.Count ?? 0;
        public T this[int index] => list[index];
    }
    // ============================== DeferredQueue ============================================================
    internal class DeferredQueue {
        long holdIdx;
        HashSet<long> holdKeys = new HashSet<long>();
        Queue<Action> queue = new Queue<Action>();
        Action<long> _release;
        long holdCount;
        public bool IsDraining { get; private set; }
        public bool IsHolding => holdCount > 0;
        public DeferredQueue() { _release = Release; }
        public SpokeHandle Hold() {
            if (holdKeys.Add(holdIdx)) FastHold();
            return SpokeHandle.Of(holdIdx++, _release);
        }
        void Release(long key) { if (holdKeys.Remove(key)) FastRelease(); }
        public void FastHold() => holdCount++;
        public void FastRelease() { if (--holdCount == 0 && !IsDraining) Drain(); }
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
}