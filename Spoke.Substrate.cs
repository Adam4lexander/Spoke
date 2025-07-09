// Spoke.Substrate.cs
// -----------------------------
// > TreeCoords
// > Node
// > Facet
// > ExecutionEngine
// > SpokeHandle
// > SpokeLogger
// > SpokePool
// > ReadOnlyList

using System;
using System.Collections.Generic;

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
    internal interface IExecutable { bool Execute(); }
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
        bool isUnmounting, isPending, isStale, isSealed = true;
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
            if (isPending || isStale) return;
            isPending = true;
            CascadeIsStale();
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
            (UntypedIdentity as Facet.IFacetFriend).Attach(this);
        }
        bool IExecutable.Execute() {
            if (isStale || Fault != null) return false;
            isPending = false; // Set now in case I trigger myself
            Unmount();
            isSealed = false;
            try { (UntypedIdentity as Facet.IFacetFriend).Mount(builder); } catch (Exception e) { Fault = e; }
            isSealed = true;
            return true;
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
        void CascadeIsStale() {
            foreach (var n in Children) {
                if (!n.isStale) { n.isStale = true; n.CascadeIsStale(); }
            }
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
    public abstract class Facet : Facet.IFacetFriend {
        SpokePool<List<SpokeBlock>> splPool = SpokePool<List<SpokeBlock>>.Create(l => l.Clear());
        internal interface IFacetFriend : ILifecycle {
            Node GetNode();
            void Attach(Node nodeAct);
            void Mount(SpokeBuilder s);
        }
        Node node;
        List<AttachBlock> attachBlocks = new List<AttachBlock>();
        List<Action> cleanupBlocks = new List<Action>();
        List<SpokeBlock> mountBlocks = new List<SpokeBlock>();
        protected TreeCoords Coords => node.Coords;
        Node IFacetFriend.GetNode() => node;
        void IFacetFriend.Attach(Node toNode) {
            if (node != null) throw new InvalidOperationException("Tried to attach a facet which was already attached");
            node = toNode;
            Action<Action> addCleanup = fn => cleanupBlocks.Add(fn);
            foreach (var fn in attachBlocks) fn?.Invoke(addCleanup);
            attachBlocks.Clear();
        }
        void IFacetFriend.Mount(SpokeBuilder s) {
            // TODO: Keyed components can unmount themselves before this function completes.
            // It's probably dangerous... For now I'm copying blocks to a new list. At least it
            // stops the modified enumeration errors.
            var spl = splPool.Get();
            foreach (var fn in mountBlocks) spl.Add(fn);
            try { foreach (var fn in spl) fn?.Invoke(s); } finally { splPool.Return(spl); }
        }
        void ILifecycle.Cleanup() {
            foreach (var fn in cleanupBlocks) fn?.Invoke();
            mountBlocks.Clear();
            cleanupBlocks.Clear();
        }
        protected void OnAttached(AttachBlock block) {
            if (node != null) throw new InvalidOperationException("Facet is already attached");
            attachBlocks.Add(block);
        }
        protected void OnMounted(SpokeBlock block) {
            if (node != null) throw new InvalidOperationException("Facet is already attached");
            mountBlocks.Add(block);
        }
        protected bool TryGetContext<T>(out T context) where T : Facet => node.TryGetContext(out context);
        protected bool TryGetComponent<T>(out T component) where T : Facet => node.TryGetComponent(out component);
        protected List<T> GetComponents<T>(List<T> storeIn = null) where T : Facet => node.GetComponents(storeIn);
        protected bool TryGetAmbient<T>(out T ambient) where T : Facet => node.TryGetAmbient(out ambient);
        protected T DynamicComponent<T>(object key, T identity) where T : Facet => node.DynamicComponent(key, identity);
        protected void DropComponent(object key) => node.DropComponent(key);
        protected void Schedule() => node.Schedule();
    }
    // ============================== ExecutionEngine ============================================================
    internal interface INodeScheduler { void Schedule(Node node); }
    public abstract class ExecutionEngine : Facet, INodeScheduler {
        protected ExecutionEngine Parent { get; private set; }
        public ExecutionEngine() {
            OnAttached(cleanup => {
                if (TryGetContext<ExecutionEngine>(out var parent)) Parent = parent;
                cleanup(() => Parent = null);
            });
        }
        void INodeScheduler.Schedule(Node node) => Schedule(node);
        protected abstract void Schedule(Node node);
        protected bool Execute(Node node) => (node as IExecutable).Execute();
        protected bool Execute(Facet facet) => Execute((facet as IFacetFriend).GetNode());
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