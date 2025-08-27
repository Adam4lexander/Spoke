using System;
using System.Collections.Generic;

namespace Spoke {

    /// <summary>
    /// An Epoch that has a dynamic collection of keyed attachments.
    /// They can attach and detach epochs at any time, outside the normal mutation windows: Init and Tick.
    /// </summary>
    public sealed class Dock : Epoch, Dock.Introspect {

        new internal interface Introspect { 
            List<Epoch> GetChildren(List<Epoch> storeIn = null); 
        }
        
        // Attached epochs are stored in this dictionary, instead of Epochs normal attachment list.
        Dictionary<object, Epoch> dynamicChildren = new();
        bool isDetaching;
        long childIndex;  // Monotonically increasing index for assigning tree-coords to attachments

        public Dock() { 
            Name = "Dock"; 
        }

        public Dock(string name) { 
            Name = name; 
        }

        /// <summary>
        /// Attaches an epoch, bound to the given key.
        /// If the key already maps to an existing epoch, that epoch is detached first.
        /// The key can be anything: a string, object reference.. Whatever is convenient.
        /// Epochs are structural descendants of the dock, they extend from the docks tree-coords, 
        /// and they're assigned the same ticker used by the dock.
        /// </summary>
        public T Call<T>(object key, T epoch) where T : Epoch {
            if (isDetaching) {
                // In case a childs cleanup function tries to attach more epochs
                throw new Exception("Cannot Call while detaching");
            }
            // Push a stack frame to reflect the docking action
            (SpokeRuntime.Local as SpokeRuntime.Friend).Push(new(SpokeRuntime.FrameKind.Dock, this));
            Drop(key);  // Detach existing epoch at they key, if any
            dynamicChildren.Add(key, epoch);
            // Monotonically increasing index ensures children ticks ordered by attach-time
            var childCoords = Coords.Extend(childIndex++);
            var childTicker = (this as Epoch.Friend).GetTicker();
            (epoch as Epoch.Friend).Attach(this, childCoords, childTicker, null);
            (SpokeRuntime.Local as SpokeRuntime.Friend).Pop();
            return epoch;
        }

        /// <summary>
        /// Detaches the epoch bound to the given key, if any.
        /// If no epoch is bound to the key, this is a no-op.
        /// </summary>
        public void Drop(object key) {
            if (!dynamicChildren.TryGetValue(key, out var child)) return;
            (child as Epoch.Friend).Detach();
            dynamicChildren.Remove(key);
        }

        protected override TickBlock Init(EpochBuilder s) {
            // When the dock is detaching, it will drop all its children, in reverse order of attachment.
            s.OnCleanup(() => {
                isDetaching = true;
                var children = (this as Introspect).GetChildren();
                for (int i = children.Count - 1; i >= 0; i--) {
                    (children[i] as Epoch.Friend).Detach();
                }
                dynamicChildren.Clear();
            });
            return null;
        }

        List<Epoch> Introspect.GetChildren(List<Epoch> storeIn) {
            storeIn = storeIn ?? new List<Epoch>();
            foreach (var child in dynamicChildren) {
                storeIn.Add(child.Value);
            }
            storeIn.Sort((a, b) => a.Coords.CompareTo(b.Coords));
            return storeIn;
        }
    }
}