using System;
using System.Collections.Generic;
using System.Text;

namespace Spoke {

    /// <summary>
    /// Introspection utilities for Spoke Epoch trees.
    /// Enables visualization tools to be built that can explore the tree structure.
    /// </summary>
    public static class SpokeIntrospect {
        static SpokePool<List<Epoch>> elPool = SpokePool<List<Epoch>>.Create(l => l.Clear());

        public static List<Epoch> GetChildren(Epoch epoch, List<Epoch> storeIn = null) {
            storeIn = storeIn ?? new List<Epoch>();
            if (epoch is Dock.Introspect d) {
                return d.GetChildren(storeIn);
            }
            return (epoch as Epoch.Introspect).GetChildren(storeIn);
        }

        public static Epoch GetParent(Epoch epoch) {
            return (epoch as Epoch.Introspect).GetParent();
        }
        
        internal static string TreeTrace(ReadOnlyList<SpokeRuntime.Frame> frames) {
            if (frames.Count == 0) return "(empty)";
            var sb = new StringBuilder();
            var es = new List<Epoch>();
            foreach (var f in frames) {
                es.Add(f.Epoch);
            }
            var roots = new List<Epoch>();
            foreach (var e in es) {
                if (!roots.Contains(e) && GetParent(e) == null) {
                    roots.Add(e);
                }
            } 
            sb.Append("<------------ Spoke Frame Trace ------------>\n").Append(StackTrace(frames)).Append("\n").Append("<------------ Spoke Tree Trace ------------>\n");
            foreach (var root in roots) {
                sb.Append(DumpTree(root, e => {
                    var label = e.ToString();
                    if (es.Contains(e)) {
                        var inds = new List<int>();
                        for (int i = 0; i < es.Count; i++) if (es[i] == e) inds.Add(i);
                        label = $"({string.Join(",", inds)})-{label}";
                    }
                    if (e.Fault != null) {
                        label = $"{label} [Faulted: {e.Fault.InnerException.GetType().Name}]";
                    }
                    return label;
                }));
                sb.Append("\n");
            }
            return sb.ToString();
        }

        static string StackTrace(ReadOnlyList<SpokeRuntime.Frame> frames) {
            if (frames.Count == 0) return "(empty)";
            var sb = new StringBuilder();
            var width = frames.Count.ToString().Length;
            for (int i = 0; i < frames.Count; i++) {
                sb.AppendLine($"{i}: {frames[i]}".PadLeft(width));
            }
            return sb.ToString();
        }

        static string DumpTree(Epoch root, Func<Epoch, string> eLabel = null) {
            var sb = new StringBuilder();
            TraverseRecurs(0, root, (depth, x) => {
                for (int i = 0; i < depth; i++) sb.Append("    ");
                sb.Append($"|--{eLabel?.Invoke(x) ?? x.ToString()}\n");
            });
            return sb.ToString();
        }

        static void TraverseRecurs(int depth, Epoch epoch, Action<int, Epoch> fn) {
            fn(depth, epoch);
            var children = GetChildren(epoch, elPool.Get());
            foreach (var c in children) TraverseRecurs(depth + 1, c, fn);
            elPool.Return(children);
        }
    }
}