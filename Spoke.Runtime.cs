// Spoke.Runtime.cs
// -----------------------------
// > SpokeRoot
// > Epoch
// > Dock
// > ExecutionEngine
// > TreeCoords
// > PackedTreeCoords128
// > SpokeHandle
// > SpokeLogger
// > FlushLogger
// > SpokePool
// > ReadOnlyList

using System;
using System.Collections.Generic;
using System.Text;

namespace Spoke {

    public delegate void EpochBlock(EpochBuilder s);

    // ============================== SpokeRoot ============================================================
    /// <summary>
    /// The root node of the call-tree must host an ExecutionEngine epoch.
    /// </summary>
    public class SpokeRoot<T> : SpokeRoot where T : ExecutionEngine {
        public T Epoch { get; private set; }
        public SpokeRoot(T epoch) : base() { Epoch = epoch; (epoch as Epoch.Friend).Init(null, null, default); }
        protected override Epoch UntypedEpoch => Epoch;
    }
    public abstract class SpokeRoot : IDisposable {
        public static SpokeRoot<T> Create<T>(T epoch) where T : ExecutionEngine => new SpokeRoot<T>(epoch);
        public void Dispose() => (UntypedEpoch as Epoch.Friend).Detach();
        protected abstract Epoch UntypedEpoch { get; }
    }
    // ============================== Epoch ============================================================
    /// <summary>
    /// A declarative, stateful execution unit that lives in the lifecycle tree.
    /// Epochs are invoked declaratively, mounted into nodes, and persist as active objects.
    /// They maintain state, respond to context, expose behaviour, and may spawn child epochs.
    /// </summary>
    public abstract class Epoch : Epoch.Friend {
        internal interface Friend { void Init(Epoch parent, Epoch prev, TreeCoords coords); void Detach(); void Remount(); void SetFault(Exception fault); List<Epoch> GetChildren(List<Epoch> storeIn = null); }
        SubEpoch attachEpoch = new();
        SubEpoch mountEpoch = new();
        public TreeCoords Coords { get; private set; }
        Epoch Parent, Prev, Next;
        bool isDetaching, isDetached;
        public Exception Fault { get; private set; }
        ExecutionEngine mountEngine;
        EpochBlock mountBlock;
        protected string Name = null;
        public override string ToString() => Name ?? GetType().Name;
        public int CompareTo(Epoch other) => Coords.CompareTo(other.Coords);
        void Friend.Init(Epoch parent, Epoch prev, TreeCoords coords) {
            Parent = parent;
            Prev = prev;
            if (prev != null) prev.Next = this;
            Coords = coords;
            mountEngine = (this as ExecutionEngine) ?? Parent.mountEngine;
            attachEpoch.Open(this, null);
            if (this is ExecutionEngine.Friend engine) engine.OnAttached(new EpochBuilder(attachEpoch));
            mountBlock = Init(new EpochBuilder(attachEpoch));
            attachEpoch.Seal();
            ScheduleMount();
        }
        void Friend.Detach() {
            if (isDetached) return;
            isDetaching = true;
            if (Next != null) (Next as Friend).Detach();
            mountEpoch.Detach();
            attachEpoch.Detach();
            if (Prev != null) Prev.Next = null;
            Next = Prev = null;
            isDetached = true;
        }
        void Friend.Remount() {
            // TODO: Keyed epochs can unmount themselves before this function completes.
            if (isDetached || isDetaching) return;
            mountEpoch.Open(this, attachEpoch);
            try { mountBlock?.Invoke(new EpochBuilder(mountEpoch)); } catch (Exception e) { Fault = e; }
            mountEpoch.Seal();
        }
        void Friend.SetFault(Exception fault) => Fault = fault;
        List<Epoch> Friend.GetChildren(List<Epoch> storeIn) {
            storeIn = storeIn ?? new List<Epoch>();
            attachEpoch.GetChildren(storeIn);
            mountEpoch.GetChildren(storeIn);
            return storeIn;
        }
        protected abstract EpochBlock Init(EpochBuilder s);
        protected void ScheduleMount() {
            if (mountEngine == null) throw new Exception("Cannot find Execution Engine");
            if (Fault != null) return;
            (mountEngine as ExecutionEngine.Friend).Schedule(this);
        }
        internal class SubEpoch {
            List<Action> onCleanup = new();
            List<SpokeHandle> handles = new();
            List<IDisposable> disposables = new();
            List<Epoch> children = new();
            bool isOpen;
            public Epoch Owner { get; private set; }
            long siblingCounter;
            public Epoch Tail { get; private set; }
            public void Open(Epoch owner, SubEpoch prev) {
                Detach();
                Owner = owner;
                siblingCounter = prev?.siblingCounter ?? 0;
                Tail = prev?.Tail;
                isOpen = true;
            }
            public void Seal() => isOpen = false;
            public void Detach() {
                for (int i = children.Count - 1; i >= 0; i--)
                    try { (children[i] as Friend).Detach(); } catch (Exception e) { SpokeError.Log($"Failed to cleanup child of '{this}': {children[i]}", e); }
                children.Clear();
                for (int i = onCleanup.Count - 1; i >= 0; i--)
                    try { onCleanup[i]?.Invoke(); } catch (Exception e) { SpokeError.Log($"Cleanup failed in '{this}'", e); }
                onCleanup.Clear();
                foreach (var handle in handles) handle.Dispose();
                handles.Clear();
                foreach (var disposable in disposables) disposable.Dispose();
                disposables.Clear();
                Owner = null;
                siblingCounter = 0;
                Tail = null;
            }
            public bool TryGetContext<T>(out T epoch) where T : Epoch {
                epoch = default;
                for (var curr = Owner.Parent; curr != null; curr = curr.Parent)
                    if (curr is T o) { epoch = o; return true; }
                return false;
            }
            public bool TryGetLexical<T>(out T epoch) where T : Epoch {
                epoch = default;
                var start = Owner.Prev ?? Owner.Parent;
                for (var anc = start; anc != null; anc = anc.Parent)
                    for (var curr = anc; curr != null; curr = curr.Prev)
                        if (curr is T o) { epoch = o; return true; }
                return false;
            }
            public SpokeHandle Use(SpokeHandle handle) {
                NoMischief(); handles.Add(handle); return handle;
            }
            public T Use<T>(T disposable) where T : IDisposable {
                NoMischief(); disposables.Add(disposable); return disposable;
            }
            public T Call<T>(T epoch) where T : Epoch {
                NoMischief();
                if (epoch.Parent != null) throw new InvalidOperationException("Tried to attach an epoch which was already attached");
                var tail = children.Count > 0 ? children[children.Count - 1] : null;
                children.Add(epoch);
                (epoch as Friend).Init(Owner, tail, Owner.Coords.Extend(siblingCounter++));
                return epoch;
            }
            public void OnCleanup(Action fn) {
                NoMischief(); onCleanup.Add(fn);
            }
            public void GetChildren(List<Epoch> storeIn) {
                foreach (var c in children) storeIn.Add(c);
            }
            void NoMischief() {
                if (!isOpen) throw new InvalidOperationException("Tried to mutate an Epoch that's been sealed for further changes.");
            }
        }
    }
    public struct EpochBuilder {
        Epoch.SubEpoch s;
        internal EpochBuilder(Epoch.SubEpoch s) { this.s = s; }
        public void Log(string msg) {
            if (s.TryGetContext(out ExecutionEngine engine)) engine.Log(msg);
        }
        public SpokeHandle Use(SpokeHandle handle) => s.Use(handle);
        public T Use<T>(T disposable) where T : IDisposable => s.Use(disposable);
        public T Call<T>(T epoch) where T : Epoch => s.Call(epoch);
        public void Call(EpochBlock block) => Call(new Scope(block));
        public bool TryGetContext<T>(out T epoch) where T : Epoch => s.TryGetContext(out epoch);
        public bool TryGetLexical<T>(out T epoch) where T : Epoch => s.TryGetLexical(out epoch);
        public void OnCleanup(Action fn) => s.OnCleanup(fn);
        class Scope : Epoch {
            EpochBlock block;
            public Scope(EpochBlock block) => this.block = block;
            protected override EpochBlock Init(EpochBuilder s) => s => block(s);
        }
    }
    // ============================== Dock ============================================================
    public class Dock : Epoch, Dock.Friend {
        new internal interface Friend { ReadOnlyList<Epoch> GetChildren(); }
        Dictionary<object, Epoch> dynamicChildren = new Dictionary<object, Epoch>();
        List<Epoch> dynamicChildrenList = new List<Epoch>();
        bool isDetaching;
        long siblingCounter;
        public Dock() { Name = "Dock"; }
        public Dock(string name) { Name = name; }
        public T Call<T>(object key, T epoch) where T : Epoch {
            if (isDetaching) throw new Exception("Cannot Call while detaching");
            Drop(key);
            dynamicChildren.Add(key, epoch);
            dynamicChildrenList.Add(epoch);
            (epoch as Epoch.Friend).Init(this, null, Coords.Extend(siblingCounter++));
            return epoch;
        }
        public void Drop(object key) {
            if (!dynamicChildren.TryGetValue(key, out var child)) return;
            var index = dynamicChildrenList.IndexOf(child);
            if (index >= 0) { (child as Epoch.Friend).Detach(); dynamicChildrenList.RemoveAt(index); }
            dynamicChildren.Remove(key);
        }
        protected override EpochBlock Init(EpochBuilder s) {
            s.OnCleanup(() => {
                isDetaching = true;
                for (int i = dynamicChildrenList.Count - 1; i >= 0; i--)
                    try { (dynamicChildrenList[i] as Epoch.Friend).Detach(); } catch (Exception e) { SpokeError.Log($"Failed to cleanup dynamic child of '{this}': {dynamicChildrenList[i]}", e); }
                dynamicChildrenList.Clear();
                dynamicChildren.Clear();
            });
            return null;
        }
        ReadOnlyList<Epoch> Friend.GetChildren() => new ReadOnlyList<Epoch>(dynamicChildrenList);
    }
    // ============================== ExecutionEngine ============================================================
    public abstract class ExecutionEngine : Epoch, ExecutionEngine.Friend {
        new internal interface Friend { void OnAttached(EpochBuilder s); void Schedule(Epoch epoch); }
        ExecutionEngine tickEngine;
        List<Epoch> incoming = new List<Epoch>();
        HashSet<Epoch> execSet = new HashSet<Epoch>();
        List<Epoch> execOrder = new List<Epoch>();
        FlushLogger flushLogger = FlushLogger.Create();
        List<string> pendingLogs = new List<string>();
        ISpokeLogger logger;
        bool isRunning;
        protected Epoch Next => execOrder.Count > 0 ? execOrder[execOrder.Count - 1] : null;
        public bool HasPending => Next != null;
        public long FlushNumber { get; private set; }
        public ExecutionEngine(ISpokeLogger logger = null) {
            this.logger = logger ?? SpokeError.DefaultLogger;
        }
        void Friend.OnAttached(EpochBuilder s) => s.TryGetContext(out tickEngine);
        void Friend.Schedule(Epoch epoch) {
            if (epoch.Fault != null) return;
            if (execSet.Contains(epoch)) return;
            incoming.Add(epoch);
            if (!isRunning) {
                var prevHasPending = HasPending;
                TakeScheduled();
                if (!prevHasPending && HasPending) OnHasPending();
            }
        }
        static readonly Comparison<Epoch> EpochComparison = (a, b) => b.CompareTo(a);
        void TakeScheduled() {
            if (incoming.Count == 0) return;
            var prevNext = Next;
            foreach (var node in incoming) if (execSet.Add(node)) execOrder.Add(node);
            incoming.Clear();
            execOrder.Sort(EpochComparison); // Reverse-order, to pop items from end of list
        }
        protected void RequestTick() {
            if (tickEngine != null) (tickEngine as Friend).Schedule(this);
            else OnTick(); // Tick immediately since we're the root engine
        }
        Epoch prevExec;
        protected Epoch RunNext() {
            if (!HasPending) return null;
            isRunning = true;
            try {
                if (prevExec != null && prevExec.CompareTo(Next) > 0) IncrementFlush();
                var exec = prevExec = Next;
                execSet.Remove(exec);
                execOrder.RemoveAt(execOrder.Count - 1);

                // Figure out if we're ticking an engine, or remounting a generic epoch
                if ((exec is ExecutionEngine eng) && eng.tickEngine == this) eng.OnTick();
                else (exec as Epoch.Friend).Remount();

                flushLogger.OnFlushNode(exec);
                TakeScheduled();
                if (!HasPending) IncrementFlush();
                return exec;
            } finally { isRunning = false; }
        }
        void IncrementFlush() {
            if (pendingLogs.Count > 0 || flushLogger.HasErrors) {
                pendingLogs.Add($"Flush: {FlushNumber}");
                flushLogger.LogFlush(logger, this, string.Join(",", pendingLogs));
            }
            flushLogger.OnFlushStart();
            pendingLogs.Clear();
            FlushNumber++;
            prevExec = null;
        }
        public void Log(string msg) => pendingLogs.Add(msg);
        protected virtual void OnHasPending() { }
        protected virtual void OnTick() { }
        protected void SetFault(Exception fault) => (this as Epoch.Friend).SetFault(fault);
        protected void Break() {
            if (!HasPending) return;
            (Next as Epoch.Friend).Detach();
            incoming.Clear(); execSet.Clear(); execOrder.Clear();
        }
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
    // ============================== FlushLogger ============================================================
    public struct FlushLogger {
        StringBuilder sb;
        HashSet<Epoch> execNodes;
        public static FlushLogger Create() => new FlushLogger {
            sb = new StringBuilder(),
            execNodes = new HashSet<Epoch>(),
        };
        public void OnFlushStart() { sb.Clear(); execNodes.Clear(); HasErrors = false; }
        public void OnFlushNode(Epoch n) { execNodes.Add(n); HasErrors |= n.Fault != null; }
        public bool HasErrors { get; private set; }
        public void LogFlush(ISpokeLogger logger, Epoch root, string msg) {
            sb.AppendLine($"[{(HasErrors ? "FLUSH ERROR" : "FLUSH")}]");
            foreach (var line in msg.Split(',')) sb.AppendLine($"-> {line}");
            PrintRoot(root);
            if (HasErrors) { PrintErrors(); logger?.Error(sb.ToString()); } else logger?.Log(sb.ToString());
        }
        void PrintErrors() {
            foreach (var c in execNodes)
                if (c.Fault != null) sb.AppendLine($"\n\n--- {NodeLabel(c)} ---\n{c.Fault}");
        }
        void PrintRoot(Epoch root) {
            var that = this;
            sb.AppendLine();
            Traverse(0, root, (depth, x) => {
                for (int i = 0; i < depth; i++) that.sb.Append("    ");
                that.sb.Append($"{that.NodeLabel(x)} {that.FaultStatus(x)}\n");
            });
        }
        string NodeLabel(Epoch node) {
            var prefix = execNodes.Contains(node) ? "(*)-" : "";
            return $"|--{prefix}{node} ";
        }
        string FaultStatus(Epoch node) {
            if (node.Fault != null)
                if (execNodes.Contains(node)) return $"[Faulted: {node.Fault.GetType().Name}]";
                else return "[Faulted]";
            return "";
        }
        void Traverse(int depth, Epoch node, Action<int, Epoch> action) {
            action?.Invoke(depth, node);
            if (node is Dock.Friend d) foreach (var child in d.GetChildren()) Traverse(depth + 1, child, action);
            if (node is Epoch.Friend e) foreach (var child in e.GetChildren()) Traverse(depth + 1, child, action);
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