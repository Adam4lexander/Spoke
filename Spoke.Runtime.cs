// Spoke.Runtime.cs
// -----------------------------
// > SpokeTree
// > Epoch
// > LambdaEpoch
// > Dock
// > SpokeEngine
// > SpokeRuntime
// > SpokeException
// > OrderedWorkStack
// > TreeCoords
// > PackedTreeCoords128
// > SpokeHandle
// > SpokeLogger
// > SpokeIntrospect
// > SpokePool
// > ReadOnlyList

using System;
using System.Collections.Generic;
using System.Text;

namespace Spoke {

    public delegate TickBlock InitBlock(EpochBuilder s);
    public delegate void TickBlock(EpochBuilder s);

    // ============================== SpokeTree ============================================================
    /// <summary>
    /// The SpokeTree hosts the root Engine of the tree. It lets you instantiate a tree, or dispose it.
    /// </summary>
    public interface SpokeTree<out T> : IDisposable where T : SpokeEngine {
        T Root { get; }
        bool IsStarted { get; }
    }
    // ============================== Epoch ============================================================
    /// <summary>
    /// A declarative, stateful execution unit that lives in the lifecycle tree.
    /// Epochs are invoked declaratively, mounted into nodes, and persist as active objects.
    /// They maintain state, respond to context, expose behaviour, and may spawn child epochs.
    /// </summary>
    public abstract class Epoch : Epoch.Friend, Epoch.Introspect {
        internal interface Scheduler { void Schedule(Epoch epoch); }
        internal interface Friend { void Attach(Epoch parent, TreeCoords coords, Scheduler scheduler, IEnumerable<object> services); void Tick(); void DetachFrom(int i); SpokeRuntime.Handle GetControlHandle(); Scheduler GetScheduler(); }
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
                if (Type == Kind.Call) try { (AsObj as Epoch).DetachFrom(0); } catch (Exception e) { SpokeError.Log($"Failed to cleanup child of '{that}': {AsObj}", e); }
            }
        }
        List<AttachRecord> attachEvents = new();
        public TreeCoords Coords { get; private set; }
        Epoch parent;
        int attachIndex, tickAttachStart = -1;
        Scheduler scheduler;
        TickBlock tickBlock;
        SpokeRuntime.Handle controlHandle;
        Action _requestTick;
        protected string Name = null;
        public Exception Fault { get; private set; }
        public override string ToString() => Name ?? GetType().Name;
        public int CompareTo(Epoch other) => Coords.CompareTo(other.Coords);
        void DetachFrom(int i) {
            if (i < 0 || i >= attachEvents.Count) return;
            while (attachEvents.Count > i) {
                attachEvents[attachEvents.Count - 1].Detach(this);
                attachEvents.RemoveAt(attachEvents.Count - 1);
            }
            tickAttachStart = Math.Min(tickAttachStart, attachEvents.Count);
        }
        void Friend.DetachFrom(int i) => DetachFrom(i);
        void Friend.Attach(Epoch parent, TreeCoords coords, Scheduler scheduler, IEnumerable<object> services) {
            _requestTick = () => { if (Fault == null) scheduler.Schedule(this); };
            this.parent = parent;
            attachIndex = parent != null ? parent.attachEvents.Count - 1 : -1;
            Coords = coords;
            this.scheduler = scheduler;
            Init(services);
        }
        void Init(IEnumerable<object> services) {
            controlHandle = (SpokeRuntime.Local as SpokeRuntime.Friend).Push(new(SpokeRuntime.FrameKind.Init, this));
            try {
                if (services != null) foreach (var x in services) attachEvents.Add(new AttachRecord(AttachRecord.Kind.Export, x));
                tickBlock = Init(new EpochBuilder(new EpochMutations(this)));
                tickAttachStart = attachEvents.Count;
            } catch (Exception e) {
                if (e is SpokeException se) {
                    if (!se.SkipMarkFaulted) Fault = se;
                    se.SkipMarkFaulted = false;
                    throw;
                }
                throw Fault = new SpokeException("Uncaught Exception in Init", e);
            } finally {
                (SpokeRuntime.Local as SpokeRuntime.Friend).Pop();
            }
            controlHandle.OnPopSelf(_requestTick);
        }
        void Friend.Tick() {
            controlHandle = (SpokeRuntime.Local as SpokeRuntime.Friend).Push(new(SpokeRuntime.FrameKind.Tick, this));
            DetachFrom(tickAttachStart);
            try {
                tickBlock?.Invoke(new EpochBuilder(new EpochMutations(this)));
            } catch (Exception e) {
                if (e is SpokeException se) {
                    if (!se.SkipMarkFaulted) Fault = se;
                    se.SkipMarkFaulted = false;
                    throw;
                }
                throw Fault = new SpokeException("Uncaught Exception in Tick", e);
            } finally {
                (SpokeRuntime.Local as SpokeRuntime.Friend).Pop();
            }
        }
        SpokeRuntime.Handle Friend.GetControlHandle() => controlHandle;
        Scheduler Friend.GetScheduler() => scheduler;
        List<Epoch> Introspect.GetChildren(List<Epoch> storeIn) {
            storeIn = storeIn ?? new List<Epoch>();
            foreach (var evt in attachEvents) if (evt.Type == AttachRecord.Kind.Call) storeIn.Add(evt.AsObj as Epoch);
            return storeIn;
        }
        Epoch Introspect.GetParent() => parent;
        protected abstract TickBlock Init(EpochBuilder s);
        internal struct EpochMutations {
            Epoch owner;
            public EpochMutations(Epoch owner) => this.owner = owner;
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
                (epoch as Friend).Attach(owner, owner.Coords.Extend(owner.attachEvents.Count - 1), (owner as Scheduler) ?? owner.scheduler, null);
                return epoch;
            }
            public T Export<T>(T obj) {
                NoMischief();
                owner.attachEvents.Add(new AttachRecord(AttachRecord.Kind.Export, obj));
                return obj;
            }
            public bool TryImport<T>(out T obj) {
                obj = default(T);
                var startIndex = owner.attachEvents.Count - 1;
                for (var anc = owner; anc != null; startIndex = anc.attachIndex, anc = anc.parent) {
                    for (var i = startIndex; i >= 0; i--) {
                        var evt = anc.attachEvents[i];
                        if (evt.Type == AttachRecord.Kind.Export && evt.AsObj is T o) {
                            obj = o;
                            return true;
                        }
                    }
                }
                return false;
            }
            public T Import<T>() {
                if (TryImport(out T obj)) return obj;
                throw new Exception($"Failed to import: {typeof(T).Name}");
            }
            public void OnCleanup(Action fn) {
                NoMischief(); owner.attachEvents.Add(new AttachRecord(AttachRecord.Kind.Cleanup, fn));
            }
            public void RequestTick() => owner.controlHandle.OnPopSelf(owner._requestTick);
            void NoMischief() {
                if (!owner.controlHandle.IsTop) throw new InvalidOperationException("Tried to mutate an Epoch that's been sealed for further changes.");
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
        public bool TryImport<T>(out T obj) => s.TryImport(out obj);
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
        Dictionary<object, Epoch> dynamicChildren = new();
        bool isDetaching;
        public Dock() { Name = "Dock"; }
        public Dock(string name) { Name = name; }
        public T Call<T>(object key, T epoch) where T : Epoch {
            if (isDetaching) throw new Exception("Cannot Call while detaching");
            (SpokeRuntime.Local as SpokeRuntime.Friend).Push(new(SpokeRuntime.FrameKind.Dock, this));
            Drop(key);
            dynamicChildren.Add(key, epoch);
            (epoch as Epoch.Friend).Attach(this, Coords.Extend(SpokeRuntime.Local.TimeStamp), (this as Epoch.Friend).GetScheduler(), null);
            (SpokeRuntime.Local as SpokeRuntime.Friend).Pop();
            return epoch;
        }
        public void Drop(object key) {
            if (!dynamicChildren.TryGetValue(key, out var child)) return;
            (child as Epoch.Friend).DetachFrom(0);
            dynamicChildren.Remove(key);
        }
        protected override TickBlock Init(EpochBuilder s) { s.OnCleanup(() => isDetaching = true); return null; }
        List<Epoch> Introspect.GetChildren(List<Epoch> storeIn) {
            storeIn = storeIn ?? new List<Epoch>();
            foreach (var child in dynamicChildren) storeIn.Add(child.Value);
            storeIn.Sort();
            return storeIn;
        }
    }
    // ============================== SpokeEngine ============================================================
    public abstract class SpokeEngine : Epoch, Epoch.Scheduler {
        Runtime runtime;
        void Epoch.Scheduler.Schedule(Epoch epoch) => runtime.Schedule(epoch);
        protected sealed override TickBlock Init(EpochBuilder s) {
            runtime = new(this);
            var root = Bootstrap(new EngineBuilder(s, runtime));
            runtime.Seal();
            s.Call(root);
            return s => runtime.TriggerTick(s);
        }
        protected abstract Epoch Bootstrap(EngineBuilder s);
        internal class Runtime {
            public Epoch Next => pending.Peek();
            public bool HasPending => Next != null;
            SpokeEngine owner;
            List<Action> onHasPending = new();
            List<Action<TickContext>> onTick = new();
            OrderedWorkStack<Epoch> pending = new((a, b) => b.CompareTo(a));
            bool isRunning, isSealed;
            Action _triggerHasPending;
            public Runtime(SpokeEngine owner) {
                this.owner = owner;
                _triggerHasPending = () => {
                    foreach (var fn in onHasPending) try { fn?.Invoke(); } catch (Exception e) { SpokeError.Log("Error invoking OnHasPending", e); }
                };
            }
            public void Seal() => isSealed = true;
            public void OnHasPending(Action fn) { NoMischief(); onHasPending.Add(fn); }
            public void OnTick(Action<TickContext> fn) { NoMischief(); onTick.Add(fn); }
            public void TriggerTick(EpochBuilder s) {
                isRunning = true;
                try { foreach (var fn in onTick) fn?.Invoke(new TickContext(s, this)); } finally { isRunning = false; }
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
                    if (!prevHasPending && HasPending) (owner as Epoch.Friend).GetControlHandle().OnPopSelf(_triggerHasPending);
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
        public bool TryImport<T>(out T obj) => s.TryImport(out obj);
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
        public Epoch PeekNext() => r.Next;
        public Epoch RunNext() => r.RunNext();
        public void RequestTick() => s.RequestTick();
    }
    // ============================== SpokeRuntime ============================================================
    public class SpokeRuntime : SpokeRuntime.Friend {
        internal interface Friend { Handle Push(Frame frame); void Pop(); void Hold(); void Release(); }
        internal static SpokeRuntime Local { get; } = new SpokeRuntime();
        public static ReadOnlyList<Frame> Frames => new ReadOnlyList<Frame>(Local.frames);
        public static string GetPrettyPrinted() => SpokeTrace.Format(Frames);
        public static void Batch(Action fn) {
            (Local as SpokeRuntime.Friend).Hold();
            try { fn(); } finally { (Local as SpokeRuntime.Friend).Release(); }
        }
        public static SpokeTree<T> SpawnTree<T>(T root, params object[] services) where T : SpokeEngine => new SpokeTreeImpl<T>(root, 0, services);
        public static SpokeTree<T> SpawnEagerTree<T>(T root, params object[] services) where T : SpokeEngine => new SpokeTreeImpl<T>(root, -1, services);
        OrderedWorkStack<SpokeTreeImpl> scheduledTrees = new((a, b) => b.CompareTo(a));
        public long TimeStamp { get; private set; }
        SpokePool<List<Action>> fnlPool = SpokePool<List<Action>>.Create(l => l.Clear());
        List<Frame> frames = new List<Frame>();
        List<long> versions = new List<long>();
        List<List<Action>> onPopSelfFrames = new List<List<Action>>();
        int layer = int.MaxValue;
        int holdCount;
        Handle Friend.Push(Frame frame) {
            frames.Add(frame);
            versions.Add(TimeStamp++);
            onPopSelfFrames.Add(fnlPool.Get());
            return new Handle(this, frames.Count - 1, versions[versions.Count - 1]);
        }
        void Friend.Pop() {
            frames.RemoveAt(frames.Count - 1);
            versions.RemoveAt(versions.Count - 1);
            var onPopSelf = onPopSelfFrames[onPopSelfFrames.Count - 1];
            onPopSelfFrames.RemoveAt(onPopSelfFrames.Count - 1);
            foreach (var fn in onPopSelf) fn?.Invoke();
            fnlPool.Return(onPopSelf);
            if (frames.Count == 0) TryFlush();
        }
        void Friend.Hold() => holdCount++;
        void Friend.Release() { holdCount--; if (holdCount == 0) TryFlush(); }
        void Schedule(SpokeTreeImpl tree) {
            var isBootstrap = frames.Count > 0 && frames[frames.Count - 1].Type == FrameKind.Bootstrap;
            if (isBootstrap && tree.Layer <= layer) { TickTree(tree); }
            else { scheduledTrees.Enqueue(tree); TryFlush(); }
        }
        void TryFlush() {
            if (holdCount > 0 || !scheduledTrees.Has) return;
            do {
                scheduledTrees.Take();
                if (scheduledTrees.Peek().Layer >= layer) return;
                TickTree(scheduledTrees.Pop());
            } while (scheduledTrees.Has);
        }
        void TickTree(SpokeTreeImpl tree) {
            var storeLayer = layer; layer = tree.Layer;
            try { tree.DoTick(); } catch (Exception e) { SpokeError.Log($"Uncaught Spoke error", e); }
            layer = storeLayer;
        }
        public enum FrameKind : byte { None, Init, Tick, Dock, Bootstrap }
        public readonly struct Frame {
            public readonly Epoch Epoch;
            public readonly FrameKind Type;
            public Frame(FrameKind type, Epoch epoch) { Type = type; Epoch = epoch; }
        }
        internal readonly struct Handle {
            public readonly SpokeRuntime Stack;
            public readonly int Index;
            readonly long version;
            public Frame Frame => IsAlive ? Stack.frames[Index] : default;
            public bool IsAlive => Stack != null && Index < Stack.frames.Count && version == Stack.versions[Index];
            public bool IsTop => IsAlive && Index == Stack.frames.Count - 1;
            public Handle(SpokeRuntime stack, int index, long version) { Stack = stack; Index = index; this.version = version; }
            public void OnPopSelf(Action fn) { if (!IsAlive) fn?.Invoke(); else Stack.onPopSelfFrames[Index].Add(fn); }
        }
        class SpokeTreeImpl<T> : SpokeTreeImpl, SpokeTree<T> where T : SpokeEngine {
            public T Root { get; private set; }
            public bool IsStarted => TimeStamp >= 0;
            Action detachAll;
            public SpokeTreeImpl(T root, int layer, params object[] services) {
                Root = root;
                Layer = layer;
                detachAll = () => (Root as Epoch.Friend).DetachFrom(0);
                if (Root == null) throw new Exception("[SpokeTree] must be given a root engine");
                if (TimeStamp >= 0) throw new Exception("[SpokeTree] already bootstrapped");
                (Local as SpokeRuntime.Friend).Push(new(FrameKind.Bootstrap, Root));
                TimeStamp = Local.TimeStamp;
                try {
                    (Root as Epoch.Friend).Attach(null, default, this, services);
                } catch (Exception e) { SpokeError.Log("[SpokeTree] uncaught error in Bootstrap", e); } finally { (Local as Friend).Pop(); }
            }
            public void Dispose() => (Root as Epoch.Friend).GetControlHandle().OnPopSelf(detachAll);
            public override void DoTick() => (Root as Epoch.Friend).Tick();
        }
        abstract class SpokeTreeImpl : IComparable<SpokeTreeImpl>, Epoch.Scheduler {
            protected long TimeStamp = -1;
            public int Layer;
            public int CompareTo(SpokeTreeImpl other) => Layer != other.Layer ? Layer.CompareTo(other.Layer) : TimeStamp.CompareTo(other.TimeStamp);
            void Epoch.Scheduler.Schedule(Epoch epoch) => Local.Schedule(this);
            public abstract void DoTick();
        }
    }
    public static class SpokeTrace {
        public static string Format(ReadOnlyList<SpokeRuntime.Frame> frames, bool includePath = false) {
            if (frames.Count == 0) return "(empty)";
            var sb = new StringBuilder();
            var width = frames.Count.ToString().Length;
            for (int i = frames.Count - 1; i >= 0; i--) AppendLine(sb, frames[i], includePath, i, width);
            return sb.ToString();
        }
        static void AppendLine(StringBuilder sb, SpokeRuntime.Frame f, bool includePath, int lineNo, int width) {
            var e = f.Epoch;
            var name = e?.ToString() ?? "<null>";
            var typeName = e != null ? e.GetType().Name : "?";
            var fault = e?.Fault != null ? " [FAULTED]" : "";
            var path = includePath && e != null ? NamePath(e) : null;
            sb.Append(lineNo.ToString().PadLeft(width));
            sb.Append($": {f.Type} {name} <{typeName}>{fault}");
            if (path != null) sb.Append($"\n    \\_ {path}");
            sb.Append("\n");
        }
        static string NamePath(Epoch e) {
            var parts = new List<string>(8);
            for (var x = e; x != null; x = SpokeIntrospect.GetParent(x)) parts.Add(x.ToString());
            parts.Reverse();
            return string.Join(" > ", parts);
        }
    }
    // ============================== SpokeException ============================================================
    // TODO: Remove strong refs to Epoch instances. Take data snapshot, enough for toString() of
    // stack trace, and a weakref to the epoch.
    public sealed class SpokeException : Exception {
        List<SpokeRuntime.Frame> stackSnapshot = new List<SpokeRuntime.Frame>();
        public bool SkipMarkFaulted;
        public ReadOnlyList<SpokeRuntime.Frame> StackSnapshot => new ReadOnlyList<SpokeRuntime.Frame>(stackSnapshot);
        public SpokeException(string msg, Exception inner) : base(msg, inner) {
            foreach (var frame in SpokeRuntime.Frames) stackSnapshot.Add(frame);
        }
        string DotNetTop() {
            var s = base.ToString();
            const string marker = "End of inner exception stack trace";
            var pos = s.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (pos < 0) return s.TrimEnd();
            var newline = s.IndexOf('\n', pos);
            var cutAt = newline >= 0 ? newline : pos + marker.Length;
            return s.Substring(0, cutAt).TrimEnd();
        }
        public override string ToString() {
            var dotnetTop = DotNetTop();
            var spokeStack = SpokeTrace.Format(StackSnapshot, includePath: false);
            return dotnetTop + "\n\n" + spokeStack;
        }
    }
    // ============================== OrderedWorkStack ============================================================
    internal class OrderedWorkStack<T> {
        List<T> incoming = new(); HashSet<T> set = new(); List<T> list = new(); Comparison<T> comp;
        public OrderedWorkStack(Comparison<T> comp) { this.comp = comp; }
        public bool Has => list.Count > 0 || incoming.Count > 0;
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
                var val = coords[i];
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
}