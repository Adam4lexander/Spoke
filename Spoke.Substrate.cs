// Spoke.Substrate.cs
// -----------------------------
// > TreeCoords
// > Node
// > Facet
// > SpokeHandle
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
    public interface NodeMutator {
        TreeCoords Coords { get; }
        void Seal();
        SpokeHandle Use(SpokeHandle handle);
        SpokeHandle Use(object key, SpokeHandle handle);
        T Component<T>(T identity) where T : Facet;
        T Component<T>(object key, T identity) where T : Facet;
        void Drop(object key);
        void OnCleanup(Action fn);
        bool TryGetContext<T>(out T context) where T : Facet;
        bool TryGetComponent<T>(out T component) where T : Facet;
        List<T> GetComponents<T>(List<T> storeIn = null) where T : Facet;
        bool TryGetAmbient<T>(out T ambient) where T : Facet;
        void ClearChildren();
    }
    public class Node<T> : Node where T : Facet {
        public T Identity { get; private set; }
        protected override Facet UntypedIdentity => Identity;
        public Node(T identity) : base() {
            Identity = identity;
        }
        protected override void OnAttached() {
            (Identity as Facet.IFacetFriend).Attach(Mutator, this);
        }
    }
    public abstract class Node : ILifecycle {
        public static Node<T> CreateRoot<T>(T identity) where T : Facet {
            var node = new Node<T>(identity);
            node.OnAttached();
            return node;
        }
        List<Node> children = new List<Node>();
        List<SpokeHandle> handles = new List<SpokeHandle>();
        Dictionary<object, Node> dynamicChildren = new Dictionary<object, Node>();
        Dictionary<object, SpokeHandle> dynamicHandles = new Dictionary<object, SpokeHandle>();
        TreeCoords coords;
        MutatorImpl mutator;
        long siblingCounter = 0;
        Node Prev, Next;
        protected abstract Facet UntypedIdentity { get; }
        protected NodeMutator Mutator => mutator;
        public Node Parent { get; private set; }
        public Node Root => Parent != null ? Parent.Root : this;
        public ReadOnlyList<Node> Children => new ReadOnlyList<Node>(children);
        protected Node() {
            mutator = new MutatorImpl(this);
        }
        public bool TryGetIdentity<T>(out T identity) where T : Facet => (identity = (UntypedIdentity as T)) != null;
        public override string ToString() => UntypedIdentity.ToString();
        void ILifecycle.Cleanup() {
            mutator.ClearChildren();
            (UntypedIdentity as ILifecycle).Cleanup();
            if (Next != null) Next.Prev = Prev;
            if (Prev != null) Prev.Next = Next;
            Next = Prev = null;
        }
        void UsedBy(Node parent, Node prev) {
            if (Parent != null) throw new Exception($"Node {this} was used by {parent}, but it's already attached to {Parent}");
            Parent = parent;
            Prev = prev;
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
            public T Component<T>(T identity) where T : Facet {
                NoMischief();
                var childNode = new Node<T>(identity);
                var prevNode = node.children.Count > 0 ? node.children[node.children.Count - 1] : node;
                if (prevNode != null) prevNode.Next = childNode;
                node.children.Add(childNode);
                childNode.UsedBy(node, prevNode);
                return childNode.Identity;
            }
            public T Component<T>(object key, T identity) where T : Facet {
                Drop(key);
                var childNode = new Node<T>(identity);
                var prevNode = node.children.Count > 0 ? node.children[node.children.Count - 1] : node;
                if (prevNode != null) prevNode.Next = childNode;
                node.dynamicChildren.Add(key, childNode);
                node.children.Add(childNode);
                childNode.UsedBy(node, prevNode);
                return childNode.Identity;
            }
            public void Drop(object key) {
                NoMischief();
                if (node.dynamicChildren.TryGetValue(key, out var child)) {
                    var index = node.children.IndexOf(child);
                    if (index >= 0) { (child as ILifecycle).Cleanup(); node.children.RemoveAt(index); }
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
                for (var curr = node.Parent; curr != null; curr = curr.Parent)
                    if (curr.TryGetIdentity<T>(out var o)) { context = o; return true; }
                return false;
            }
            public bool TryGetComponent<T>(out T component) where T : Facet {
                if (node.TryGetIdentity(out component)) return true;
                foreach (var n in node.Children) {
                    if (n.Mutator.TryGetComponent(out component)) return true;
                }
                return false;
            }
            public bool TryGetAmbient<T>(out T ambient) where T : Facet {
                ambient = default(T);
                for (var curr = node.Prev; curr != null; curr = curr.Prev)
                    if (curr.TryGetIdentity<T>(out var o)) { ambient = o; return true; }
                return false;
            }
            public List<T> GetComponents<T>(List<T> storeIn = null) where T : Facet {
                storeIn = storeIn ?? new List<T>();
                if (node.TryGetIdentity<T>(out var component)) storeIn.Add(component);
                foreach (var n in node.Children) {
                    n.Mutator.GetComponents(storeIn);
                }
                return storeIn;
            }
            public void ClearChildren() {
                isChildrenDisposing = true;
                for (int i = cleanupFuncs.Count - 1; i >= 0; i--)
                    try { cleanupFuncs[i]?.Invoke(); } catch (Exception e) { SpokeError.Log($"Cleanup failed in '{this}'", e); }
                cleanupFuncs.Clear();
                foreach (var triggerChild in node.handles) triggerChild.Dispose();
                node.handles.Clear();
                for (int i = node.children.Count - 1; i >= 0; i--)
                    try { (node.children[i] as ILifecycle).Cleanup(); } catch (Exception e) { SpokeError.Log($"Failed to cleanup child of '{this}': {node.children[i]}", e); }
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
    // ============================== Facet ============================================================
    public abstract class Facet : Facet.IFacetFriend {
        internal interface IFacetFriend : ILifecycle {
            Node GetNode();
            void Attach(NodeMutator nodeMut, Node nodeAct);
        }
        NodeMutator _owner;
        protected NodeMutator Owner {
            get {
                if (_owner == null) throw new InvalidOperationException("Facet is not yet attached to the tree");
                return _owner;
            }
        }
        Node nodeActual;
        Node IFacetFriend.GetNode() => nodeActual;
        void IFacetFriend.Attach(NodeMutator nodeMutable, Node nodeActual) {
            if (_owner != null) throw new InvalidOperationException("Tried to attach a facet which was already attached");
            _owner = nodeMutable;
            this.nodeActual = nodeActual;
            Attached();
        }
        void ILifecycle.Cleanup() => Cleanup();
        protected abstract void Attached();
        protected abstract void Cleanup();
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