using System;
using System.Collections.Generic;

namespace Spoke {

    /// <summary>
    /// The Spoke runtime, providing VM-like capabilities.
    /// It's the global orchestrator for Spoke trees in Auto flush mode. Pending trees are flushed synchronously,
    /// and their order, or whether they flush nested, is managed by SpokeRuntime.
    /// Takes control of the thread immediately when a tree requests a tick, and returns control only after all
    /// pending trees have been drained.
    /// </summary>
    public sealed class SpokeRuntime : SpokeRuntime.Friend {

        internal interface Friend { 
            Handle Push(Frame frame); 
            void Pop(); 
            void Hold(); 
            void Release(); 
            void Schedule(Epoch epoch); 
            void TickTree(SpokeTree tree); 
        }
        
        // Intended for this to become a ThreadStatic in the future. So trees can be driven on multiple threads.
        internal static SpokeRuntime Local { get; } = new SpokeRuntime();

        // The virtual Spoke call stack
        public static ReadOnlyList<Frame> Frames => new ReadOnlyList<Frame>(Local.frames);

        /// <summary>
        /// Holds the runtime from flushing any trees until after the action completes.
        /// If we're already mid-flush, this holds the runtime from initiating a nested flush.
        /// </summary>
        public static void Batch(Action fn) {
            (Local as SpokeRuntime.Friend).Hold();
            try { 
                fn(); 
            } finally { 
                (Local as SpokeRuntime.Friend).Release(); 
            }
        }

        /// <summary>Monotonically increasing timestamp, incremented on each stack frame pushed.</summary>
        public long TimeStamp { get; private set; }

        // Priority queue of trees pending flush
        OrderedWorkStack<SpokeTree> scheduledTrees = new((a, b) => b.CompareTo(a));

        SpokePool<List<Action>> fnlPool = SpokePool<List<Action>>.Create(l => l.Clear());
        List<Frame> frames = new List<Frame>();
        List<long> versions = new List<long>(); // Determines validity of stack handles

        // onPopSelfFrames[i] holds a list of actions to invoke when frames[i] is popped
        List<List<Action>> onPopSelfFrames = new List<List<Action>>();
        int layer = int.MaxValue; // Flush layer of currently flushing tree, lower values are higher priority
        int holdCount;

        // Pushes a Spoke stack frame, returning a control handle to it.
        Handle Friend.Push(Frame frame) {
            frames.Add(frame);
            versions.Add(TimeStamp++);
            onPopSelfFrames.Add(fnlPool.Get());
            return new Handle(this, frames.Count - 1, versions[versions.Count - 1]);
        }

        // Pops the top Spoke stack frame.
        void Friend.Pop() {
            frames.RemoveAt(frames.Count - 1);
            versions.RemoveAt(versions.Count - 1);
            var onPopSelf = onPopSelfFrames[onPopSelfFrames.Count - 1];
            onPopSelfFrames.RemoveAt(onPopSelfFrames.Count - 1);
            foreach (var fn in onPopSelf) {
                fn?.Invoke();
            }
            fnlPool.Return(onPopSelf);
        }

        // Increments the hold count, preventing any tree flushes until Release is called.
        void Friend.Hold() {
            holdCount++;
        }

        // Decrements the hold count, and if it reaches zero, attempts to flush any pending trees.
        void Friend.Release() { 
            holdCount--;
            if (holdCount == 0) {
                TryFlush();
            }
        }

        // Schedules a tree for flushing, and attempts to flush
        void Friend.Schedule(Epoch epoch) {
            if (!(epoch is SpokeTree tree)) {
                throw new Exception("SpokeRuntime can only schedule trees");
            }
            scheduledTrees.Enqueue(tree); 
            TryFlush();
        }

        // Attempts to flush pending trees, if we're not being held and there are trees scheduled.
        // May be called recursively. If more trees are schedule during a flush, then decide if they flush nested.
        void TryFlush() {
            if (holdCount > 0 || !scheduledTrees.Has) return;
            do {
                var top = scheduledTrees.Peek();
                var isPendingEagerTick = (top as SpokeTree.Friend).IsPendingEagerTick();
                // Newly spawned trees may flush nested inside trees of equal flush layer
                if (isPendingEagerTick && top.FlushLayer > layer) return;
                // Or else it must have a higher priority flush layer for nested flush
                else if (!isPendingEagerTick && top.FlushLayer >= layer) return;
                // Tick the tree. SpokeTree always flushes in Auto mode
                (this as Friend).TickTree(scheduledTrees.Pop());
            } while (scheduledTrees.Has);
        }

        // Delivers a tick to the given tree.
        // Sets the runtimes current flush layer to the tree's flush layer, and restores it afterwards.
        // The runtimes flush layer determines outcome whether TryFlush() flushes nested or not.
        void Friend.TickTree(SpokeTree tree) {
            var storeLayer = layer; 
            layer = Math.Min(tree.FlushLayer, layer);
            try {
                (tree as Epoch.Friend).Tick(); 
            } catch (Exception e) { 
                SpokeError.Log($"Uncaught Spoke error", e); 
            }
            layer = storeLayer;
            if (frames.Count == 0) {
                TryFlush();
            }
        }

        public enum FrameKind : byte { None, Init, Tick, Dock, Bootstrap }

        /// <summary>
        /// A frame on the Spoke call stack.
        /// </summary>
        public readonly struct Frame {
            public readonly Epoch Epoch;
            public readonly FrameKind Type;

            public Frame(FrameKind type, Epoch epoch) { 
                Type = type; 
                Epoch = epoch; 
            }

            public override string ToString() {
                if (Type == FrameKind.None) return "<null>";
                var typeName = Epoch.GetType().Name;
                typeName = typeName.IndexOf('`') >= 0 ? Epoch.GetType().Name.Substring(0, typeName.IndexOf('`')) : Epoch.GetType().Name;
                return $"{Type} {Epoch} <{typeName}>{(Epoch.Fault != null ? $"[Faulted: {Epoch.Fault.InnerException.GetType().Name}]" : "")}";
            }
        }

        // Control handle for a Spoke stack frame.
        // Can be used to check if the frame is still alive, and to register a callback when it is popped.
        internal readonly struct Handle {
            public readonly SpokeRuntime Stack;
            public readonly int Index;
            readonly long version;  // Must match versions[Index] to be valid

            public Frame Frame => IsAlive ? Stack.frames[Index] : default;
            public bool IsAlive => Stack != null && Index < Stack.frames.Count && version == Stack.versions[Index];
            public bool IsTop => IsAlive && Index == Stack.frames.Count - 1;

            public Handle(SpokeRuntime stack, int index, long version) { 
                Stack = stack; 
                Index = index; 
                this.version = version; 
            }

            // Registers a callback to be invoked when this frame is popped, or immediately if it's already dead.
            public void OnPopSelf(Action fn) {
                if (!IsAlive) {
                    fn?.Invoke();
                } else {
                    Stack.onPopSelfFrames[Index].Add(fn);
                }
            }
        }
    }
}