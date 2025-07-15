// Spoke.Runtime.cs
// -----------------------------
// > TreeCoords
// > Node
// > Epoch
// > ExecutionEngine
// > SpokeHandle
// > SpokeLogger
// > FlushLogger
// > SpokePool
// > ReadOnlyList
// > DeferredQueue

using System;
using System.Collections.Generic;
using System.Text;

namespace Spoke {

    public delegate void AttachBlock(Action<Action> cleanup);
    public delegate void EpochBlock(EpochBuilder s);

    // ============================== TreeCoords ============================================================
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
    // ============================== Node ============================================================
    /// <summary>
    /// A runtime container for a mounted Epoch within the lifecycle tree.
    /// Nodes form the structure of declarative execution, managing position, context, and children.
    /// Each node controls mounting, scheduling, and cleanup for its Epoch and subtree.
    /// </summary>
    public class Node<T> : Node where T : Epoch {
        public T Epoch { get; private set; }
        public override Epoch UntypedEpoch => Epoch;
        public Node(T epoch) : base() {
            Epoch = epoch;
        }
    }
    public interface EpochBuilder {
        SpokeHandle Use(SpokeHandle handle);
        T Use<T>(T disposable) where T : IDisposable;
        T Call<T>(T epoch) where T : Epoch;
        void Call(EpochBlock block);
        public bool TryGetLexical<T>(out T context) where T : Epoch;
        void OnCleanup(Action fn);
    }
    public abstract class Node : Node.Friend {
        internal interface Friend { void Remount(); }
        enum MountStatus { Sealed, Open, Unmounting }
        public static Node<T> CreateRoot<T>(T epoch) where T : ExecutionEngine {
            var node = new Node<T>(epoch);
            node.Attach(null);
            return node;
        }
        List<Node> children = new List<Node>();
        Dictionary<object, Node> dynamicChildren = new Dictionary<object, Node>();
        List<Node> dynamicChildrenList = new List<Node>();
        List<SpokeHandle> handles = new List<SpokeHandle>();
        List<IDisposable> disposables = new List<IDisposable>();
        List<Action> mountCleanupFuncs = new List<Action>();
        public TreeCoords Coords { get; private set; }
        EpochBuilderImpl builder;
        long siblingCounter = 0;
        Node Prev, Next;
        MountStatus mountStatus = MountStatus.Sealed;
        bool isDetaching;
        public abstract Epoch UntypedEpoch { get; }
        public Node Parent { get; private set; }
        public Node Root => Parent != null ? Parent.Root : this;
        public ReadOnlyList<Node> Children => new ReadOnlyList<Node>(children);
        public ReadOnlyList<Node> DynamicChildren => new ReadOnlyList<Node>(dynamicChildrenList);
        public Exception Fault { get; private set; }
        protected Node() {
            builder = new EpochBuilderImpl(this);
        }
        public bool TryGetEpoch<T>(out T epoch) where T : Epoch => (epoch = (UntypedEpoch as T)) != null;
        public override string ToString() => UntypedEpoch.ToString();
        void Attach(Node parent) {
            if (Parent != null) throw new Exception($"Node {this} was used by {parent}, but it's already attached to {Parent}");
            Parent = parent;
            if (parent != null) {
                Prev = parent.children.Count > 1 ? parent.children[parent.children.Count - 2] : null;
                if (Prev != null) Prev.Next = this;
                Coords = parent.Coords.Extend(parent.siblingCounter++);
            }
            (UntypedEpoch as Epoch.Friend).Attach(this);
        }
        void AttachDynamic(Node parent) {
            if (Parent != null) throw new Exception($"Node {this} was used by {parent}, but it's already attached to {Parent}");
            Parent = parent;
            Coords = parent.Coords.Extend(parent.siblingCounter++);
            (UntypedEpoch as Epoch.Friend).Attach(this);
        }
        void Cleanup() {
            isDetaching = true;
            Unmount();
            (UntypedEpoch as Epoch.Friend).Cleanup();
            for (int i = dynamicChildrenList.Count - 1; i >= 0; i--)
                try { dynamicChildrenList[i].Cleanup(); } catch (Exception e) { SpokeError.Log($"Failed to cleanup dynamic child of '{this}': {dynamicChildrenList[i]}", e); }
            dynamicChildrenList.Clear();
            dynamicChildren.Clear();
            if (Next != null) Next.Prev = Prev;
            if (Prev != null) Prev.Next = Next;
            Next = Prev = null;
        }
        void Friend.Remount() {
            Unmount();
            mountStatus = MountStatus.Open;
            try { (UntypedEpoch as Epoch.Friend).Mount(builder); } catch (Exception e) { Fault = e; }
            mountStatus = MountStatus.Sealed;
        }
        public bool TryGetSubEpoch<T>(out T epoch) where T : Epoch {
            if (TryGetEpoch(out epoch)) return true;
            foreach (var n in Children) if (n.TryGetSubEpoch(out epoch)) return true;
            return false;
        }
        public List<T> GetSubEpochs<T>(List<T> storeIn = null) where T : Epoch {
            storeIn = storeIn ?? new List<T>();
            if (TryGetEpoch<T>(out var epoch)) storeIn.Add(epoch);
            foreach (var n in Children) n.GetSubEpochs(storeIn);
            return storeIn;
        }
        public bool TryGetContext<T>(out T epoch) where T : Epoch {
            epoch = default;
            for (var curr = Parent; curr != null; curr = curr.Parent)
                if (curr.TryGetEpoch<T>(out var o)) { epoch = o; return true; }
            return false;
        }
        public bool TryGetLexical<T>(out T epoch) where T : Epoch {
            epoch = default;
            var start = Prev ?? Parent;
            for (var anc = start; anc != null; anc = anc.Parent)
                for (var curr = anc; curr != null; curr = curr.Prev)
                    if (curr.TryGetEpoch<T>(out var o)) { epoch = o; return true; }
            return false;
        }
        public T CallDynamic<T>(object key, T epoch) where T : Epoch {
            if (isDetaching) throw new Exception("Cannot CallDynamic while detaching");
            DropDynamic(key);
            var childNode = new Node<T>(epoch);
            dynamicChildren.Add(key, childNode);
            dynamicChildrenList.Add(childNode);
            childNode.AttachDynamic(this);
            return childNode.Epoch;
        }
        public void DropDynamic(object key) {
            if (!dynamicChildren.TryGetValue(key, out var child)) return;
            var index = dynamicChildrenList.IndexOf(child);
            if (index >= 0) { child.Cleanup(); dynamicChildrenList.RemoveAt(index); }
            dynamicChildren.Remove(key);
        }
        void Unmount() {
            mountStatus = MountStatus.Unmounting;
            for (int i = children.Count - 1; i >= 0; i--)
                try { children[i].Cleanup(); } catch (Exception e) { SpokeError.Log($"Failed to cleanup child of '{this}': {children[i]}", e); }
            children.Clear();
            if (dynamicChildren.Count == 0) siblingCounter = 0;
            for (int i = mountCleanupFuncs.Count - 1; i >= 0; i--)
                try { mountCleanupFuncs[i]?.Invoke(); } catch (Exception e) { SpokeError.Log($"Cleanup failed in '{this}'", e); }
            mountCleanupFuncs.Clear();
            foreach (var handle in handles) handle.Dispose();
            handles.Clear();
            foreach (var disposable in disposables) disposable.Dispose();
            disposables.Clear();
            mountStatus = MountStatus.Sealed;
        }
        class EpochBuilderImpl : EpochBuilder {
            Node node;
            public EpochBuilderImpl(Node node) {
                this.node = node;
            }
            public SpokeHandle Use(SpokeHandle handle) {
                NoMischief(); node.handles.Add(handle); return handle;
            }
            public T Use<T>(T disposable) where T : IDisposable {
                NoMischief(); node.disposables.Add(disposable); return disposable;
            }
            public T Call<T>(T epoch) where T : Epoch {
                NoMischief();
                var childNode = new Node<T>(epoch);
                node.children.Add(childNode);
                childNode.Attach(node);
                return childNode.Epoch;
            }
            public void Call(EpochBlock block) => Call(new Scope(block));
            public bool TryGetLexical<T>(out T epoch) where T : Epoch => node.TryGetLexical(out epoch);
            public void OnCleanup(Action fn) { NoMischief(); node.mountCleanupFuncs.Add(fn); }
            void NoMischief() {
                if (node.mountStatus == MountStatus.Unmounting) throw new Exception("Cannot mutate Node while it's unmounting");
                if (node.mountStatus == MountStatus.Sealed) throw new Exception("Cannot mutate Node after it's sealed");
            }
        }
        class Scope : Epoch {
            EpochBlock block;
            public Scope(EpochBlock block) => this.block = block;
            protected override void OnMounted(EpochBuilder s) => block(s);
        }
    }
    // ============================== Epoch ============================================================
    /// <summary>
    /// A declarative, stateful execution unit that lives in the lifecycle tree.
    /// Epochs are invoked declaratively, mounted into nodes, and persist as active objects.
    /// They maintain state, respond to context, expose behaviour, and may spawn child epochs.
    /// </summary>
    public abstract class Epoch : Epoch.Friend {
        internal interface Friend { void Attach(Node hostNode); void Cleanup(); void Mount(EpochBuilder s); Node GetNode(); }
        Node node;
        List<Action> cleanupBlocks = new List<Action>();
        ExecutionEngine mountEngine;
        protected string Name = null;
        protected TreeCoords Coords => node.Coords;
        public override string ToString() => Name ?? base.ToString();
        void Friend.Attach(Node hostNode) {
            if (node != null) throw new InvalidOperationException("Tried to attach an epoch which was already attached");
            node = hostNode;
            if (!node.TryGetEpoch(out mountEngine)) mountEngine = node.Parent?.UntypedEpoch.mountEngine;
            Action<Action> onDetached = fn => cleanupBlocks.Add(fn);
            if (this is ExecutionEngine.Friend engine) engine.OnAttached();
            OnAttached(onDetached);
            ScheduleMount();
        }
        void Friend.Cleanup() {
            foreach (var fn in cleanupBlocks) fn?.Invoke();
            cleanupBlocks.Clear();
        }
        void Friend.Mount(EpochBuilder s) {
            // TODO: Keyed epochs can unmount themselves before this function completes.
            OnMounted(s);
        }
        Node Friend.GetNode() => node;
        protected virtual void OnAttached(Action<Action> onDetach) { }
        protected virtual void OnMounted(EpochBuilder s) { }
        protected bool TryGetContext<T>(out T epoch) where T : Epoch => node.TryGetContext(out epoch);
        protected bool TryGetSubEpoch<T>(out T epoch) where T : Epoch => node.TryGetSubEpoch(out epoch);
        protected List<T> GetSubEpochs<T>(List<T> storeIn = null) where T : Epoch => node.GetSubEpochs(storeIn);
        protected bool TryGetLexical<T>(out T epoch) where T : Epoch => node.TryGetLexical(out epoch);
        protected T CallDynamic<T>(object key, T epoch) where T : Epoch => node.CallDynamic(key, epoch);
        protected void DropDynamic(object key) => node.DropDynamic(key);
        protected void ScheduleMount() {
            if (mountEngine == null) throw new Exception("Cannot find Execution Engine");
            if (node.Fault != null) return;
            (mountEngine as ExecutionEngine.Friend).Schedule(node);
        }
    }
    // ============================== ExecutionEngine ============================================================
    public enum FlushMode { Immediate, Manual }
    public abstract class ExecutionEngine : Epoch, ExecutionEngine.Friend {
        new internal interface Friend { void OnAttached(); void Schedule(Node node); }
        Node node => (this as Epoch.Friend).GetNode();
        ExecutionEngine tickEngine;
        public FlushMode FlushMode = FlushMode.Immediate;
        List<Node> incoming = new List<Node>();
        HashSet<Node> execSet = new HashSet<Node>();
        List<Node> execOrder = new List<Node>();
        FlushLogger flushLogger = FlushLogger.Create();
        List<string> pendingLogs = new List<string>();
        DeferredQueue deferred = new DeferredQueue();
        ISpokeLogger logger;
        protected Node Next => execOrder.Count > 0 ? execOrder[execOrder.Count - 1] : null;
        protected bool HasPending => Next != null;
        public bool IsFlushing { get; private set; }
        Action _flush;
        public ExecutionEngine(FlushMode flushMode, ISpokeLogger logger = null) {
            _flush = Flush;
            this.logger = logger ?? new ConsoleSpokeLogger();
        }
        public SpokeHandle Hold() => deferred.Hold();
        void Friend.OnAttached() => TryGetContext(out tickEngine);
        void Friend.Schedule(Node node) {
            if (node.Fault != null) return;
            if (execSet.Contains(node)) return;
            incoming.Add(node);
            if (!IsFlushing) {
                TakeScheduled();
                if (HasPending && FlushMode == FlushMode.Immediate) ScheduleTick();
            }
        }
        static readonly Comparison<Node> NodeComparison = (a, b) => b.Coords.CompareTo(a.Coords);
        void TakeScheduled() {
            if (incoming.Count == 0) return;
            foreach (var node in incoming) if (execSet.Add(node)) execOrder.Add(node);
            incoming.Clear();
            execOrder.Sort(NodeComparison); // Reverse-order, to pop items from end of list
        }
        void OnTick() => BeginFlush();
        protected void ScheduleTick() {
            if (tickEngine != null) (tickEngine as Friend).Schedule(node);
            else OnTick(); // Tick immediately since we're the root engine
        }
        protected void BeginFlush() { if (deferred.IsEmpty) deferred.Enqueue(_flush); }
        void Flush() {
            if (IsFlushing) return;
            IsFlushing = true;
            try {
                for (long pass = 0; Next != null; pass++) {
                    Node prev = null;
                    flushLogger.OnFlushStart();
                    while (Next != null) {
                        if (prev != null && prev.Coords.CompareTo(Next.Coords) > 0) break; // new pass
                        if (!ContinueFlush(pass)) break;
                        var exec = Next;
                        execSet.Remove(exec);
                        execOrder.RemoveAt(execOrder.Count - 1);

                        // Figure out if we're ticking an engine, or remounting a generic epoch
                        if (exec.TryGetEpoch<ExecutionEngine>(out var eng) && eng.tickEngine == this) eng.OnTick();
                        else (exec as Node.Friend).Remount();

                        if (exec.Fault != null) OnFaulted(exec);
                        flushLogger.OnFlushNode(exec);
                        TakeScheduled();
                    }
                    if (pendingLogs.Count > 0 || flushLogger.HasErrors) {
                        pendingLogs.Add($"Flush Pass: {pass}");
                        flushLogger.LogFlush(logger, string.Join(",", pendingLogs));
                    }
                    pendingLogs.Clear();
                }
            } catch (Exception ex) {
                SpokeError.Log("Internal Flush Error: ", ex);
            } finally {
                IsFlushing = false;
                pendingLogs.Clear();
            }
        }
        public void LogNextFlush(string msg) => pendingLogs.Add(msg);
        protected abstract bool ContinueFlush(long nPasses);
        protected virtual void OnFaulted(Node node) { }
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
    }
    // ============================== FlushLogger ============================================================
    public struct FlushLogger {
        StringBuilder sb;
        List<Node> runHistory;
        HashSet<Node> roots;
        public static FlushLogger Create() => new FlushLogger {
            sb = new StringBuilder(),
            runHistory = new List<Node>(),
            roots = new HashSet<Node>()
        };
        public void OnFlushStart() { sb.Clear(); roots.Clear(); runHistory.Clear(); HasErrors = false; }
        public void OnFlushNode(Node n) { runHistory.Add(n); HasErrors |= n.Fault != null; }
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
                var runIndex = that.runHistory.IndexOf(x);
                for (int i = 0; i < depth; i++) that.sb.Append("    ");
                that.sb.Append($"{that.NodeLabel(x)} {that.FaultStatus(x)}\n");
            });
        }
        string NodeLabel(Node node) {
            var indexes = new List<int>();
            for (int i = 0; i < runHistory.Count; i++)
                if (ReferenceEquals(runHistory[i], node)) indexes.Add(i);
            var indexStr = indexes.Count > 0 ? $"({string.Join(",", indexes)})-" : "";
            return $"|--{indexStr}{node} ";
        }
        string FaultStatus(Node node) {
            if (node.Fault != null)
                if (runHistory.Contains(node)) return $"[Faulted: {node.Fault.GetType().Name}]";
                else return "[Faulted]";
            return "";
        }
        void Traverse(int depth, Node node, Action<int, Node> action) {
            action?.Invoke(depth, node);
            foreach (var child in node.Children) Traverse(depth + 1, child, action);
            foreach (var child in node.DynamicChildren) Traverse(depth + 1, child, action);
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
        public bool IsDraining { get; private set; }
        public bool IsEmpty => queue.Count == 0 && !IsDraining;
        public DeferredQueue() { _release = Release; }
        public SpokeHandle Hold() {
            holdKeys.Add(holdIdx);
            return SpokeHandle.Of(holdIdx++, _release);
        }
        void Release(long key) {
            if (holdKeys.Remove(key)) if (holdKeys.Count == 0 && !IsDraining) Drain();
        }
        public void Enqueue(Action action) {
            queue.Enqueue(action);
            if (holdKeys.Count == 0 && !IsDraining) Drain();
        }
        void Drain() {
            IsDraining = true;
            while (queue.Count > 0) queue.Dequeue()();
            IsDraining = false;
        }
    }
}