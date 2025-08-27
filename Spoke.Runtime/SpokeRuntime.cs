using System;
using System.Collections.Generic;

namespace Spoke {

    public sealed class SpokeRuntime : SpokeRuntime.Friend {

        internal interface Friend { 
            Handle Push(Frame frame); 
            void Pop(); 
            void Hold(); 
            void Release(); 
            void Schedule(Epoch epoch); 
            void TickTree(SpokeTree tree); 
        }
        
        internal static SpokeRuntime Local { get; } = new SpokeRuntime();

        public static ReadOnlyList<Frame> Frames => new ReadOnlyList<Frame>(Local.frames);

        public static void Batch(Action fn) {
            (Local as SpokeRuntime.Friend).Hold();
            try { 
                fn(); 
            } finally { 
                (Local as SpokeRuntime.Friend).Release(); 
            }
        }

        public long TimeStamp { get; private set; }

        OrderedWorkStack<SpokeTree> scheduledTrees = new((a, b) => b.CompareTo(a));
        SpokePool<List<Action>> fnlPool = SpokePool<List<Action>>.Create(l => l.Clear());
        List<Frame> frames = new List<Frame>();
        List<long> versions = new List<long>();
        List<List<Action>> onPopSelfFrames = new List<List<Action>>();
        int layer = int.MaxValue;
        int holdCount;

        Handle Friend.Push(Frame frame) {
            frames.Add(frame);
            versions.Add(TimeStamp++);
            onPopSelfFrames.Add(fnlPool.Get());
            return new Handle(this, frames.Count - 1, versions[versions.Count - 1]);
        }

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

        void Friend.Hold() {
            holdCount++;
        }

        void Friend.Release() { 
            holdCount--;
            if (holdCount == 0) {
                TryFlush();
            }
        }

        void Friend.Schedule(Epoch epoch) {
            if (!(epoch is SpokeTree tree)) {
                throw new Exception("SpokeRuntime can only schedule trees");
            }
            scheduledTrees.Enqueue(tree); TryFlush();
        }

        void TryFlush() {
            if (holdCount > 0 || !scheduledTrees.Has) return;
            do {
                var top = scheduledTrees.Peek();
                var isPendingEagerTick = (top as SpokeTree.Friend).IsPendingEagerTick();
                if (isPendingEagerTick && top.FlushLayer > layer) return;
                else if (!isPendingEagerTick && top.FlushLayer >= layer) return;
                (this as Friend).TickTree(scheduledTrees.Pop());
            } while (scheduledTrees.Has);
        }

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

        internal readonly struct Handle {
            public readonly SpokeRuntime Stack;
            public readonly int Index;
            readonly long version;

            public Frame Frame => IsAlive ? Stack.frames[Index] : default;
            public bool IsAlive => Stack != null && Index < Stack.frames.Count && version == Stack.versions[Index];
            public bool IsTop => IsAlive && Index == Stack.frames.Count - 1;

            public Handle(SpokeRuntime stack, int index, long version) { 
                Stack = stack; 
                Index = index; 
                this.version = version; 
            }

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