using System;
using System.Collections.Generic;

namespace Spoke {

    /// <summary>
    /// An Epoch that has a dynamic collection of keyed attachments.
    /// They can attach and detach epochs at any time, outside the normal mutation windows: Init and Tick.
    /// </summary>
    public sealed class Dock : Epoch, Dock.Friend, Dock.Introspect {

        new internal interface Friend {
            void Compact();
        }

        new internal interface Introspect {
            List<Epoch> GetChildren(List<Epoch> storeIn = null);
        }

        // Attached epochs are stored in slots, in attach order, instead of Epoch's normal
        // attachment list. A slot's index is the child's tree-coordinate within the dock.
        // Dropped children leave a null slot behind, reclaimed by Compact.
        List<KeyValuePair<object, Epoch>> childSlots = new();
        Dictionary<object, int> slotByKey = new();
        bool isDetaching;

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
        /// Epochs are structural descendants of the dock, they extend from the dock's tree-coords,
        /// and they're assigned the same ticker used by the dock.
        /// </summary>
        public T Call<T>(object key, T epoch) where T : Epoch {
            if (isDetaching) {
                // In case a child's cleanup function tries to attach more epochs
                throw new Exception("Cannot Call while detaching");
            }
            // Push a stack frame to reflect the docking action
            (SpokeRuntime.Local as SpokeRuntime.Friend).Push(new(SpokeRuntime.FrameKind.Dock, this));
            try {
                Drop(key);  // Detach existing epoch at the key, if any
                // Sweep once the slots outgrow the packed coordinate range and at least half are reclaimable
                if (childSlots.Count > 255 && slotByKey.Count * 2 <= childSlots.Count) {
                    (this as Friend).Compact();
                }
                // Slot order matches attach order, so children tick ordered by attach-time
                slotByKey.Add(key, childSlots.Count);
                childSlots.Add(new(key, epoch));
                var childTicker = (this as Epoch.Friend).GetTicker();
                // The child is slotted before Attach, so if its Init throws it stays docked
                // (faulted), and its partial cleanup still runs on Drop or dock detach.
                (epoch as Epoch.Friend).Attach(this, childSlots.Count - 1, childTicker, null);
            } finally {
                (SpokeRuntime.Local as SpokeRuntime.Friend).Pop();
            }
            return epoch;
        }

        /// <summary>
        /// Detaches the epoch bound to the given key, if any.
        /// If no epoch is bound to the key, this is a no-op.
        /// </summary>
        public void Drop(object key) {
            if (!slotByKey.TryGetValue(key, out var slot)) return;
            var child = childSlots[slot].Value;
            // If the child is mid-execution (dropping or re-keying itself), defer its detach until it
            // finishes so its full teardown still runs; an idle child detaches immediately.
            (child as Epoch.Friend).GetControlHandle().OnPopSelf((child as Epoch.Friend).Detach);
            childSlots[slot] = default;
            slotByKey.Remove(key);
            if (slotByKey.Count == 0) childSlots.Clear();
        }

        // Sweep out dropped slots, renumbering the survivors so new attachments pack again
        void Friend.Compact() {
            var write = 0;
            for (var read = 0; read < childSlots.Count; read++) {
                var slot = childSlots[read];
                if (slot.Value == null) continue;
                childSlots[write] = slot;
                slotByKey[slot.Key] = write;
                (slot.Value as Epoch.Friend).Reindex(write);
                write++;
            }
            childSlots.RemoveRange(write, childSlots.Count - write);
        }

        protected override TickBlock Init(EpochBuilder s) {
            // When the dock is detaching, it will drop all its children, in reverse order of attachment.
            s.OnCleanup(() => {
                isDetaching = true;
                for (var i = childSlots.Count - 1; i >= 0; i--) {
                    if (childSlots[i].Value == null) continue;
                    (childSlots[i].Value as Epoch.Friend).Detach();
                }
                childSlots.Clear();
                slotByKey.Clear();
            });
            return null;
        }

        List<Epoch> Introspect.GetChildren(List<Epoch> storeIn) {
            storeIn = storeIn ?? new List<Epoch>();
            foreach (var slot in childSlots) {
                if (slot.Value != null) storeIn.Add(slot.Value);
            }
            return storeIn;
        }
    }
}
