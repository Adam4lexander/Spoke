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
        protected override Epoch UntypedEpoch => Epoch;
        public Node(T epoch) : base() {
            Epoch = epoch;
        }
    }
    internal interface ILifecycle { void Attach(Node parent); void Cleanup(); }
    internal interface IExecutable { void Execute(); }
    public interface EpochBuilder {
        SpokeHandle Use(SpokeHandle handle);
        T Use<T>(T disposable) where T : IDisposable;
        T Call<T>(T epoch) where T : Epoch;
        void Call(EpochBlock block);
        public bool TryGetLexical<T>(out T context) where T : Epoch;
        void OnCleanup(Action fn);
    }
    public abstract class Node : ILifecycle, IExecutable {
        public static Node<T> CreateRoot<T>(T epoch) where T : Epoch {
            var node = new Node<T>(epoch);
            (node as ILifecycle).Attach(null);
            (node as IExecutable).Execute();
            return node;
        }
        List<Node> children = new List<Node>();
        Dictionary<object, Node> dynamicChildren = new Dictionary<object, Node>();
        List<SpokeHandle> handles = new List<SpokeHandle>();
        List<IDisposable> disposables = new List<IDisposable>();
        List<Action> mountCleanupFuncs = new List<Action>();
        public TreeCoords Coords { get; private set; }
        EpochBuilderImpl builder;
        long siblingCounter = 0;
        Node Prev, Next;
        ExecutionEngine engine;
        bool isUnmounting, isPending, isSealed = true;
        protected abstract Epoch UntypedEpoch { get; }
        public Node Parent { get; private set; }
        public Node Root => Parent != null ? Parent.Root : this;
        public ReadOnlyList<Node> Children => new ReadOnlyList<Node>(children);
        public Exception Fault { get; private set; }
        protected Node() {
            builder = new EpochBuilderImpl(this);
        }
        public bool TryGetEpoch<T>(out T epoch) where T : Epoch => (epoch = (UntypedEpoch as T)) != null;
        public override string ToString() => UntypedEpoch.ToString();
        public void Schedule(bool isDeferred) {
            if (engine == null) throw new Exception("Cannot find Execution Engine");
            if (isPending) return;
            isPending = true;
            (engine as INodeScheduler).Schedule(this, isDeferred);
        }
        void ILifecycle.Attach(Node parent) {
            if (Parent != null) throw new Exception($"Node {this} was used by {parent}, but it's already attached to {Parent}");
            Parent = parent;
            if (parent != null) {
                Prev = parent.children.Count > 1 ? parent.children[parent.children.Count - 2] : null;
                if (Prev != null) Prev.Next = this;
                Coords = parent.Coords.Extend(parent.siblingCounter++);
            }
            TryGetContext(out engine);
            (UntypedEpoch as ILifecycle).Attach(this);
        }
        void ILifecycle.Cleanup() {
            Unmount();
            (UntypedEpoch as ILifecycle).Cleanup();
            if (Next != null) Next.Prev = Prev;
            if (Prev != null) Prev.Next = Next;
            Next = Prev = null;
        }
        void IExecutable.Execute() {
            isPending = false; // Set now in case I trigger myself
            Unmount();
            isSealed = false;
            try { (UntypedEpoch as IEpochFriend).Mount(builder); } catch (Exception e) { Fault = e; }
            isSealed = true;
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
            DropDynamic(key);
            var childNode = new Node<T>(epoch);
            dynamicChildren.Add(key, childNode);
            children.Add(childNode);
            (childNode as ILifecycle).Attach(this);
            return childNode.Epoch;
        }
        public void DropDynamic(object key) {
            if (isUnmounting) throw new Exception("Cannot mutate Node while it's unmounting");
            if (!dynamicChildren.TryGetValue(key, out var child)) return;
            var index = children.IndexOf(child);
            if (index >= 0) { (child as ILifecycle).Cleanup(); children.RemoveAt(index); }
            dynamicChildren.Remove(key);
        }
        void Unmount() {
            isUnmounting = true;
            for (int i = mountCleanupFuncs.Count - 1; i >= 0; i--)
                try { mountCleanupFuncs[i]?.Invoke(); } catch (Exception e) { SpokeError.Log($"Cleanup failed in '{this}'", e); }
            mountCleanupFuncs.Clear();
            foreach (var handle in handles) handle.Dispose();
            handles.Clear();
            foreach (var disposable in disposables) disposable.Dispose();
            disposables.Clear();
            for (int i = children.Count - 1; i >= 0; i--)
                try { (children[i] as ILifecycle).Cleanup(); } catch (Exception e) { SpokeError.Log($"Failed to cleanup child of '{this}': {children[i]}", e); }
            children.Clear();
            siblingCounter = 0;
            dynamicChildren.Clear();
            isSealed = false;
            isUnmounting = false;
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
                (childNode as ILifecycle).Attach(node);
                return childNode.Epoch;
            }
            public void Call(EpochBlock block) => Call(new Scope(block));
            public bool TryGetLexical<T>(out T epoch) where T : Epoch => node.TryGetLexical(out epoch);
            public void OnCleanup(Action fn) { NoMischief(); node.mountCleanupFuncs.Add(fn); }
            void NoMischief() {
                if (node.isUnmounting) throw new Exception("Cannot mutate Node while it's unmounting");
                if (node.isSealed) throw new Exception("Cannot mutate Node after it's sealed");
            }
        }
        class Scope : Epoch { public Scope(EpochBlock block) => OnMounted(block); }
    }
    // ============================== Epoch ============================================================
    internal interface IEpochFriend { void Mount(EpochBuilder s); }
    /// <summary>
    /// A declarative, stateful execution unit that lives in the lifecycle tree.
    /// Epochs are invoked declaratively, mounted into nodes, and persist as active objects.
    /// They maintain state, respond to context, expose behaviour, and may spawn child epochs.
    /// </summary>
    public abstract class Epoch : IEpochFriend, ILifecycle {
        Node node;
        List<AttachBlock> attachBlocks = new List<AttachBlock>();
        List<Action> cleanupBlocks = new List<Action>();
        EpochBlock mountBlock;
        bool isDeferred;
        protected TreeCoords Coords => node.Coords;
        public Epoch(bool isDeferred = true) {
            this.isDeferred = isDeferred;
        }
        void ILifecycle.Attach(Node parent) {
            if (node != null) throw new InvalidOperationException("Tried to attach an epoch which was already attached");
            node = parent;
            Action<Action> addCleanup = fn => cleanupBlocks.Add(fn);
            foreach (var fn in attachBlocks) fn?.Invoke(addCleanup);
            attachBlocks.Clear();
            if (node.Parent != null) Schedule();
        }
        void ILifecycle.Cleanup() {
            foreach (var fn in cleanupBlocks) fn?.Invoke();
            mountBlock = null;
            cleanupBlocks.Clear();
        }
        void IEpochFriend.Mount(EpochBuilder s) {
            // TODO: Keyed epochs can unmount themselves before this function completes.
            mountBlock?.Invoke(s);
        }
        protected void OnAttached(AttachBlock block) {
            if (node != null) throw new InvalidOperationException("Epoch is already attached");
            attachBlocks.Add(block);
        }
        protected void OnMounted(EpochBlock block) {
            if (node != null) throw new InvalidOperationException("Epoch is already attached");
            if (mountBlock != null) throw new InvalidOperationException("Epoch already has a mount block");
            mountBlock = block;
        }
        protected bool TryGetContext<T>(out T epoch) where T : Epoch => node.TryGetContext(out epoch);
        protected bool TryGetSubEpoch<T>(out T epoch) where T : Epoch => node.TryGetSubEpoch(out epoch);
        protected List<T> GetSubEpochs<T>(List<T> storeIn = null) where T : Epoch => node.GetSubEpochs(storeIn);
        protected bool TryGetLexical<T>(out T epoch) where T : Epoch => node.TryGetLexical(out epoch);
        protected T CallDynamic<T>(object key, T epoch) where T : Epoch => node.CallDynamic(key, epoch);
        protected void DropDynamic(object key) => node.DropDynamic(key);
        protected void Schedule() { node.Schedule(isDeferred); }
    }
    // ============================== ExecutionEngine ============================================================
    public enum FlushMode { Immediate, Manual }
    internal interface INodeScheduler { void Schedule(Node node, bool isDeferred); }
    public abstract class ExecutionEngine : Epoch, INodeScheduler {
        public FlushMode FlushMode = FlushMode.Immediate;
        List<Node> incoming = new List<Node>();
        HashSet<Node> execSet = new HashSet<Node>();
        List<Node> execOrder = new List<Node>();
        FlushLogger flushLogger = FlushLogger.Create();
        List<string> pendingLogs = new List<string>();
        DeferredQueue deferred = DeferredQueue.Create();
        ISpokeLogger logger;
        protected ExecutionEngine Parent { get; private set; }
        protected Node Next => execOrder.Count > 0 ? execOrder[execOrder.Count - 1] : null;
        protected bool HasPending => Next != null;
        bool isFlushing;
        Action _flush;
        public ExecutionEngine(FlushMode flushMode, ISpokeLogger logger = null) {
            _flush = Flush;
            this.logger = logger ?? new ConsoleSpokeLogger();
            OnAttached(cleanup => {
                if (TryGetContext<ExecutionEngine>(out var parent)) Parent = parent;
                cleanup(() => Parent = null);
            });
        }
        public void Hold() => deferred.Hold();
        public void Release() => deferred.Release();
        void INodeScheduler.Schedule(Node node, bool isDeferred) {
            if (!isDeferred && isFlushing) {
                (node as IExecutable).Execute();
                flushLogger.OnFlushNode(node);
                return;
            }
            if (execSet.Contains(node)) return;
            incoming.Add(node);
            if (!isFlushing) {
                TakeScheduled();
                if (HasPending && FlushMode == FlushMode.Immediate) BeginFlush();
            }
        }
        static readonly Comparison<Node> NodeComparison = (a, b) => b.Coords.CompareTo(a.Coords);
        void TakeScheduled() {
            if (incoming.Count == 0) return;
            var prevIsPending = HasPending;
            foreach (var node in incoming) if (execSet.Add(node)) execOrder.Add(node);
            incoming.Clear();
            execOrder.Sort(NodeComparison); // Reverse-order, to pop items from end of list
        }
        protected void BeginFlush() { if (deferred.IsEmpty) deferred.Enqueue(_flush); }
        void Flush() {
            if (isFlushing) return;
            isFlushing = true;
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
                        (exec as IExecutable).Execute();
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
                isFlushing = false;
                pendingLogs.Clear();
            }
        }
        public void LogNextFlush(string msg) => pendingLogs.Add(msg);
        protected abstract bool ContinueFlush(long nPasses);
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
}