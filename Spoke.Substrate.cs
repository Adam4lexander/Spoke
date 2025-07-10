// Spoke.Substrate.cs
// -----------------------------
// > TreeCoords
// > Node
// > Facet
// > ExecutionEngine
// > DeferredQueue
// > SpokeHandle
// > SpokeLogger
// > FlushLogger
// > SpokePool
// > ReadOnlyList

using System;
using System.Collections.Generic;
using System.Text;

namespace Spoke {

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
    internal interface ILifecycle { void Cleanup(); }
    public interface SpokeBuilder {
        SpokeHandle Use(SpokeHandle handle);
        T Component<T>(T identity) where T : Facet;
        void OnCleanup(Action fn);
    }
    public class Node<T> : Node where T : Facet {
        public T Identity { get; private set; }
        protected override Facet UntypedIdentity => Identity;
        public Node(T identity) : base() {
            Identity = identity;
        }
    }
    internal interface IExecutable { void Execute(); }
    public abstract class Node : ILifecycle, IExecutable {
        public static Node<T> CreateRoot<T>(T identity) where T : Facet {
            var node = new Node<T>(identity);
            node.OnAttached();
            (node as IExecutable).Execute();
            return node;
        }
        List<Node> children = new List<Node>();
        List<SpokeHandle> handles = new List<SpokeHandle>();
        Dictionary<object, Node> dynamicChildren = new Dictionary<object, Node>();
        List<Action> mountCleanupFuncs = new List<Action>();
        public TreeCoords Coords { get; private set; }
        SpokeBuilderImpl builder;
        long siblingCounter = 0;
        Node Prev, Next;
        ExecutionEngine engine;
        bool isUnmounting, isPending, isSealed = true;
        protected abstract Facet UntypedIdentity { get; }
        public Node Parent { get; private set; }
        public Node Root => Parent != null ? Parent.Root : this;
        public ReadOnlyList<Node> Children => new ReadOnlyList<Node>(children);
        public Exception Fault { get; private set; }
        protected Node() {
            builder = new SpokeBuilderImpl(this);
        }
        public bool TryGetIdentity<T>(out T identity) where T : Facet => (identity = (UntypedIdentity as T)) != null;
        public override string ToString() => UntypedIdentity.ToString();
        public void Schedule() {
            if (engine == null) throw new Exception("Cannot find Execution Engine");
            if (isPending) return;
            isPending = true;
            (engine as INodeScheduler).Schedule(this);
        }
        void ILifecycle.Cleanup() {
            Unmount();
            (UntypedIdentity as ILifecycle).Cleanup();
            if (Next != null) Next.Prev = Prev;
            if (Prev != null) Prev.Next = Next;
            Next = Prev = null;
        }
        void UsedBy(Node parent, Node prev) {
            if (Parent != null) throw new Exception($"Node {this} was used by {parent}, but it's already attached to {Parent}");
            Parent = parent;
            Prev = prev;
            Coords = parent.Coords.Extend(parent.siblingCounter++);
            OnAttached();
        }
        void OnAttached() {
            TryGetContext(out engine);
            (UntypedIdentity as IFacetFriend).Attach(this);
        }
        void IExecutable.Execute() {
            isPending = false; // Set now in case I trigger myself
            Unmount();
            isSealed = false;
            try { (UntypedIdentity as IFacetFriend).Mount(builder); } catch (Exception e) { Fault = e; }
            isSealed = true;
        }
        public bool TryGetComponent<T>(out T component) where T : Facet {
            if (TryGetIdentity(out component)) return true;
            foreach (var n in Children) {
                if (n.TryGetComponent(out component)) return true;
            }
            return false;
        }
        public List<T> GetComponents<T>(List<T> storeIn = null) where T : Facet {
            storeIn = storeIn ?? new List<T>();
            if (TryGetIdentity<T>(out var component)) storeIn.Add(component);
            foreach (var n in Children) {
                n.GetComponents(storeIn);
            }
            return storeIn;
        }
        public bool TryGetContext<T>(out T context) where T : Facet {
            context = default(T);
            for (var curr = Parent; curr != null; curr = curr.Parent)
                if (curr.TryGetIdentity<T>(out var o)) { context = o; return true; }
            return false;
        }
        public bool TryGetAmbient<T>(out T ambient) where T : Facet {
            ambient = default(T);
            var start = Prev ?? Parent;
            for (var anc = start; anc != null; anc = anc.Parent)
                for (var curr = anc; curr != null; curr = curr.Prev)
                    if (curr.TryGetIdentity<T>(out var o)) { ambient = o; return true; }
            return false;
        }
        public T DynamicComponent<T>(object key, T identity) where T : Facet {
            DropComponent(key);
            var childNode = new Node<T>(identity);
            var prevNode = children.Count > 0 ? children[children.Count - 1] : this;
            if (prevNode != null) prevNode.Next = childNode;
            dynamicChildren.Add(key, childNode);
            children.Add(childNode);
            childNode.UsedBy(this, prevNode);
            return childNode.Identity;
        }
        public void DropComponent(object key) {
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
            foreach (var triggerChild in handles) triggerChild.Dispose();
            handles.Clear();
            for (int i = children.Count - 1; i >= 0; i--)
                try { (children[i] as ILifecycle).Cleanup(); } catch (Exception e) { SpokeError.Log($"Failed to cleanup child of '{this}': {children[i]}", e); }
            children.Clear();
            siblingCounter = 0;
            dynamicChildren.Clear();
            isSealed = false;
            isUnmounting = false;
        }
        class SpokeBuilderImpl : SpokeBuilder {
            Node node;
            public SpokeBuilderImpl(Node node) {
                this.node = node;
            }
            public SpokeHandle Use(SpokeHandle handle) {
                NoMischief(); node.handles.Add(handle); return handle;
            }
            public T Component<T>(T identity) where T : Facet {
                NoMischief();
                var childNode = new Node<T>(identity);
                var prevNode = node.children.Count > 0 ? node.children[node.children.Count - 1] : null;
                if (prevNode != null) prevNode.Next = childNode;
                node.children.Add(childNode);
                childNode.UsedBy(node, prevNode);
                return childNode.Identity;
            }
            public void OnCleanup(Action fn) { NoMischief(); node.mountCleanupFuncs.Add(fn); }
            void NoMischief() {
                if (node.isUnmounting) throw new Exception("Cannot mutate Node while it's unmounting");
                if (node.isSealed) throw new Exception("Cannot mutate Node after it's sealed");
            }
        }
    }
    // ============================== Facet ============================================================
    public delegate void AttachBlock(Action<Action> cleanup);
    public delegate void SpokeBlock(SpokeBuilder s);
    internal interface IFacetFriend : ILifecycle {
        Node GetNode();
        void Attach(Node nodeAct);
        void Mount(SpokeBuilder s);
    }
    public abstract class Facet : IFacetFriend {
        Node node;
        List<AttachBlock> attachBlocks = new List<AttachBlock>();
        List<Action> cleanupBlocks = new List<Action>();
        SpokeBlock mountBlock;
        bool isDeferred;
        protected TreeCoords Coords => node.Coords;
        public Facet(bool isDeferred = true) {
            this.isDeferred = isDeferred;
        }
        Node IFacetFriend.GetNode() => node;
        void IFacetFriend.Attach(Node toNode) {
            if (node != null) throw new InvalidOperationException("Tried to attach a facet which was already attached");
            node = toNode;
            Action<Action> addCleanup = fn => cleanupBlocks.Add(fn);
            foreach (var fn in attachBlocks) fn?.Invoke(addCleanup);
            attachBlocks.Clear();
            if (node.Parent != null) Schedule();
        }
        void IFacetFriend.Mount(SpokeBuilder s) {
            // TODO: Keyed components can unmount themselves before this function completes.
            mountBlock?.Invoke(s);
        }
        void ILifecycle.Cleanup() {
            foreach (var fn in cleanupBlocks) fn?.Invoke();
            mountBlock = null;
            cleanupBlocks.Clear();
        }
        protected void OnAttached(AttachBlock block) {
            if (node != null) throw new InvalidOperationException("Facet is already attached");
            attachBlocks.Add(block);
        }
        protected void OnMounted(SpokeBlock block) {
            if (node != null) throw new InvalidOperationException("Facet is already attached");
            if (mountBlock != null) throw new InvalidOperationException("Facet already has a mount block");
            mountBlock = block;
        }
        protected bool TryGetContext<T>(out T context) where T : Facet => node.TryGetContext(out context);
        protected bool TryGetComponent<T>(out T component) where T : Facet => node.TryGetComponent(out component);
        protected List<T> GetComponents<T>(List<T> storeIn = null) where T : Facet => node.GetComponents(storeIn);
        protected bool TryGetAmbient<T>(out T ambient) where T : Facet => node.TryGetAmbient(out ambient);
        protected T DynamicComponent<T>(object key, T identity) where T : Facet => node.DynamicComponent(key, identity);
        protected void DropComponent(object key) => node.DropComponent(key);
        protected void Schedule() { if (isDeferred) node.Schedule(); else (node as IExecutable).Execute(); }
    }
    // ============================== ExecutionEngine ============================================================
    internal interface INodeScheduler { void Schedule(Node node); }
    public abstract class ExecutionEngine : Facet, INodeScheduler {
        List<Node> incoming = new List<Node>();
        HashSet<Node> execSet = new HashSet<Node>();
        List<Node> execOrder = new List<Node>();
        DeferredQueue deferTakeScheduled = DeferredQueue.Create();
        Action _takeScheduled;
        protected ExecutionEngine Parent { get; private set; }
        protected Node Next => execOrder.Count > 0 ? execOrder[execOrder.Count - 1] : null;
        protected bool HasPending => Next != null;
        public ExecutionEngine() {
            _takeScheduled = TakeScheduled;
            OnAttached(cleanup => {
                if (TryGetContext<ExecutionEngine>(out var parent)) Parent = parent;
                cleanup(() => Parent = null);
            });
        }
        void INodeScheduler.Schedule(Node node) {
            if (execSet.Contains(node)) return;
            incoming.Add(node);
            deferTakeScheduled.Enqueue(_takeScheduled);
            if (deferTakeScheduled.IsEmpty) OnPending();
        }
        static readonly Comparison<Node> NodeComparison = (a, b) => b.Coords.CompareTo(a.Coords);
        void TakeScheduled() {
            if (incoming.Count == 0) return;
            var prevIsPending = HasPending;
            foreach (var node in incoming) if (execSet.Add(node)) execOrder.Add(node);
            incoming.Clear();
            execOrder.Sort(NodeComparison); // Reverse-order, to pop items from end of list
        }
        protected Node ExecuteNext() {
            if (execOrder.Count == 0) return null;
            deferTakeScheduled.Hold();
            var exec = Next;
            execSet.Remove(exec);
            execOrder.RemoveAt(execOrder.Count - 1);
            (exec as IExecutable).Execute();
            deferTakeScheduled.Release();
            return exec;
        }
        protected abstract void OnPending();
    }
    // ============================== DeferredQueue ============================================================
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
}