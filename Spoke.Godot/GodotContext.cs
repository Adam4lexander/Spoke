using Godot;

namespace Spoke {

    /// <summary>
    /// The Godot context of a SpokeTree: it carries the Node hosting the tree, and doubles as the
    /// tree's logger — messages are tagged with the node's path, so you can find the culprit.
    ///
    /// The Spoke node base classes export one into their tree automatically. Extensions that need to
    /// reach the host node — s.OnProcess and s.AddChild — retrieve it with s.Import&lt;GodotContext&gt;().
    /// Hand-spawned trees can pass their own, or none at all.
    /// </summary>
    public class GodotContext : ISpokeLogger {

        /// <summary>The node hosting the tree. Null for trees spawned outside a node.</summary>
        public readonly Node Node;

        public GodotContext(Node node = null) {
            Node = node;
        }

        /// <summary>Prints to the Output panel.</summary>
        public void Log(string msg)
            => GD.Print($"{Tag}{msg}");

        /// <summary>
        /// Pushes to the Debugger panel. Unlike GD.PrintErr this gets a stack trace and a clickable
        /// entry in Debugger > Errors, which is what you want when a tree faults.
        /// </summary>
        public void Error(string msg)
            => GD.PushError($"{Tag}{msg}");

        // GetPath() only means anything while the node is in the tree, and errors when it isn't.
        string Tag {
            get {
                if (Node == null || !GodotObject.IsInstanceValid(Node)) return "";
                return Node.IsInsideTree() ? $"[{Node.GetPath()}] " : $"[{Node.Name}] ";
            }
        }
    }
}
