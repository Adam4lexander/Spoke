using System;
using System.Collections.Generic;

namespace Spoke {

    /// <summary>Delegate type for computing a memo's value</summary>
    public delegate T MemoBlock<T>(MemoBuilder s);
    /// <summary>Delegate type for computing a memo's value from its previous value</summary>
    public delegate T MemoFoldBlock<T>(MemoBuilder s, T prev);

    /// <summary>
    /// Abstract base class for Memo<T>
    /// - Use Memo.Create() to create a memo that isn't attached to a tree. Dispose it when you're done
    /// - Use s.Memo() to create a memo that's disposed with the block that created it
    ///
    /// Memos aren't part of any SpokeTree. They form their own dependency graph, which settles before
    /// the Spoke runtime ticks any tree. So an effect only ever sees memos that are up to date.
    ///
    /// When a signal changes, every memo downstream of it is marked before anything recomputes:
    /// - Dirty, if it read that signal directly. It must recompute
    /// - Check, if it only reads memos that are in doubt. It recomputes if one of them really changes
    /// A memo that's read while marked brings itself up to date first. That's what lets the graph
    /// settle in any order without a memo ever computing from a value that's out of date.
    /// </summary>
    public abstract class Memo : IDisposable, Memo.Friend, SpokeRuntime.PreFlush {

        // Internal methods used by MemoBuilder only
        internal interface Friend {
            void AddDynamicTrigger(ITrigger trigger);
            void OnCleanup(Action fn);
        }

        /// <summary>Creates a memo that recomputes when any signal it reads changes</summary>
        public static Memo<T> Create<T>(MemoBlock<T> selector, params ITrigger[] triggers)
            => new Memo<T>("Memo", selector, triggers);

        /// <summary>Creates a memo that recomputes when any signal it reads changes</summary>
        public static Memo<T> Create<T>(string name, MemoBlock<T> selector, params ITrigger[] triggers)
            => new Memo<T>(name, selector, triggers);

        /// <summary>Creates a memo that folds its previous value into the next, starting from seed</summary>
        public static Memo<T> Create<T>(T seed, MemoFoldBlock<T> selector, params ITrigger[] triggers)
            => new Memo<T>("Memo", seed, selector, triggers);

        /// <summary>Creates a memo that folds its previous value into the next, starting from seed</summary>
        public static Memo<T> Create<T>(string name, T seed, MemoFoldBlock<T> selector, params ITrigger[] triggers)
            => new Memo<T>(name, seed, selector, triggers);

        enum Status : byte { Clean, Check, Dirty }

        // Guards against a memo that keeps invalidating itself, which would never let the graph settle
        const int MaxRunsPerPreFlush = 1000;

        string name;
        DependencyTracker tracker;
        Trigger invalidated;        // Tells the memos that depend on this one when it stops being Clean. Null until one does
        // Memos this one depends on, in the order they were read, each with a subscription to its
        // invalidated trigger. The static ones come first and are kept. The rest are rebuilt every run
        List<(Memo memo, SpokeHandle handle)> upstream = new List<(Memo, SpokeHandle)>();
        int staticUpstreamCount;
        List<Action> cleanups;      // Declared by the last run. Null until one is declared
        Action _markCheck;
        Action _settle;
        Exception fault;            // Thrown by the last run, rethrown to anyone reading the memo
        Status status = Status.Dirty; // Dirty until the first run
        bool isComputing;
        bool isValidating;
        bool isQueued;              // Waiting in the runtime's pre-flush queue
        bool isDisposed;
        long preFlushSeen = -1;     // SpokeRuntime.PreFlushCount when the runtime last ran this memo
        int runsThisPreFlush;

        internal Memo(string name, IEnumerable<ITrigger> triggers) {
            this.name = name;
            _markCheck = MarkCheck;
            _settle = Settle;
            tracker = new DependencyTracker(MarkDirty);
            if (triggers != null) {
                foreach (var trigger in triggers) {
                    tracker.AddStatic(trigger);
                    AddUpstream(trigger);
                }
            }
            staticUpstreamCount = upstream.Count;
        }

        public override string ToString() {
            return name ?? GetType().Name;
        }

        /// <summary>
        /// Stops the memo from recomputing, and runs any cleanup its last run declared.
        /// It keeps its last value.
        /// </summary>
        public void Dispose() {
            if (isDisposed) return;
            isDisposed = true;
            tracker.Dispose();
            DropUpstream(0);
            RunCleanups();
        }

        /// <summary>Computes the next value. Returns true if it's different to the last.</summary>
        protected abstract bool Compute(MemoBuilder s);

        /// <summary>Notifies subscribers that the value changed.</summary>
        protected abstract void Notify();

        // Called by the subclass constructor, once it's ready to compute.
        // The first run waits for the code that created the memo to finish, the same as a child
        // epoch would. Outside of Spoke there's nothing to wait for, so it runs right away.
        // Reading the memo before then runs it early.
        protected void Start() {
            (SpokeRuntime.Local as SpokeRuntime.Friend).AfterCurrentFrame(_settle);
        }

        // Brings the memo up to date, so its value can be read.
        protected void EnsureFresh(bool rethrowFault) {
            if (isComputing) {
                throw new InvalidOperationException($"Memo '{this}' was read while it was computing. A memo can't depend on itself.");
            }
            // Read by something it asked to update. Its current value is all there is to give
            if (isValidating) return;
            Update();
            if (rethrowFault && fault != null) {
                throw new InvalidOperationException($"Memo '{this}' faulted on its last run", fault);
            }
        }

        // A signal this memo read has changed. It must recompute.
        void MarkDirty() {
            if (isDisposed) return;
            var wasClean = status == Status.Clean;
            status = Status.Dirty;
            if (wasClean) Invalidate();
        }

        // A memo this one read is in doubt. It must recompute if that memo really changes.
        void MarkCheck() {
            if (isDisposed || status != Status.Clean) return;
            status = Status.Check;
            Invalidate();
        }

        // This memo just stopped being Clean. Queue it to run before any tree does, and put the
        // memos downstream of it in doubt.
        void Invalidate() {
            var runtime = SpokeRuntime.Local as SpokeRuntime.Friend;
            // Held, so that everything downstream is marked before any of it runs
            runtime.Hold();
            try {
                Enqueue();
                invalidated?.Invoke();
            } finally {
                runtime.Release();
            }
        }

        void Enqueue() {
            if (isQueued) return;
            isQueued = true;
            (SpokeRuntime.Local as SpokeRuntime.Friend).SchedulePreFlush(this);
        }

        void SpokeRuntime.PreFlush.Run() {
            isQueued = false;
            if (isDisposed || status == Status.Clean) return;
            // Marked again while it's in the middle of updating. It queues itself when it's done
            if (isComputing || isValidating) return;
            var preFlush = (SpokeRuntime.Local as SpokeRuntime.Friend).PreFlushCount;
            if (preFlushSeen != preFlush) {
                preFlushSeen = preFlush;
                runsThisPreFlush = 0;
            }
            if (++runsThisPreFlush > MaxRunsPerPreFlush) {
                // Left faulted and Clean, so the graph can settle. It runs again when a dependency changes
                status = Status.Clean;
                fault = new InvalidOperationException($"Memo '{this}' kept invalidating itself - possible infinite loop");
                SpokeError.Log($"Memo '{this}' faulted", fault);
                return;
            }
            Settle();
        }

        // Brings the memo up to date with nobody waiting to read it. So there's nobody to throw a
        // fault to, and it's logged instead.
        void Settle() {
            if (Update() && fault != null) {
                SpokeError.Log($"Memo '{this}' faulted", fault);
            }
        }

        // Brings the memo up to date. Returns true if that took a run.
        bool Update() {
            if (isDisposed || status == Status.Clean) return false;
            if (status == Status.Check) {
                // Only memos upstream are in doubt. Bring them up to date in the order they were
                // read. One that really changes marks this memo Dirty, and the rest can wait for
                // the run to read them.
                isValidating = true;
                try {
                    for (var i = 0; i < upstream.Count && status == Status.Check; i++) {
                        try {
                            upstream[i].memo.EnsureFresh(rethrowFault: false);
                        } catch (Exception) {
                            status = Status.Dirty; // The run will meet the same error, and record it
                        }
                    }
                } finally {
                    isValidating = false;
                }
                if (status == Status.Check) {
                    status = Status.Clean; // Nothing upstream changed after all
                    return false;
                }
            }
            Recompute();
            // Marked again by something its own run changed. It can't settle until it runs again
            if (status != Status.Clean) Enqueue();
            return true;
        }

        void Recompute() {
            status = Status.Clean; // A dependency that fires from here on marks it again
            isComputing = true;
            var hadFault = fault != null;
            var changed = false;
            DropUpstream(staticUpstreamCount);
            tracker.BeginDynamic();
            try {
                RunCleanups();
                changed = Compute(new MemoBuilder(this));
                fault = null;
            } catch (Exception e) {
                fault = e;
            } finally {
                tracker.EndDynamic();
                isComputing = false;
            }
            // Subscribers are notified last, when the memo is safe to read again. Faulting or
            // recovering counts as a change, because readers need to find out
            if (changed || hadFault != (fault != null)) Notify();
        }

        // Subscribes to hear when a memo this one depends on is in doubt.
        void AddUpstream(ITrigger trigger) {
            if (!(trigger is Memo memo) || memo == this) return;
            if (memo.invalidated == null) memo.invalidated = Trigger.Create();
            upstream.Add((memo, (memo.invalidated as Trigger.Friend).AddDependant(_markCheck)));
        }

        void DropUpstream(int keep) {
            while (upstream.Count > keep) {
                upstream[upstream.Count - 1].handle.Dispose();
                upstream.RemoveAt(upstream.Count - 1);
            }
        }

        void RunCleanups() {
            if (cleanups == null) return;
            for (var i = cleanups.Count - 1; i >= 0; i--) {
                try {
                    cleanups[i]?.Invoke();
                } catch (Exception e) {
                    SpokeError.Log($"Cleanup failed in memo '{this}'", e);
                }
            }
            cleanups.Clear();
        }

        void Friend.AddDynamicTrigger(ITrigger trigger) {
            if (!isComputing) {
                throw new InvalidOperationException("MemoBuilder is only valid while its MemoBlock is running");
            }
            tracker.AddDynamic(trigger);
            AddUpstream(trigger);
        }

        void Friend.OnCleanup(Action fn) {
            if (!isComputing) {
                throw new InvalidOperationException("MemoBuilder is only valid while its MemoBlock is running");
            }
            (cleanups ?? (cleanups = new List<Action>())).Add(fn);
        }
    }

    /// <summary>
    /// A computed reactive value which updates when any of its triggers fire
    /// It takes a selector function (type MemoBlock<T>) which returns the new value
    /// It's best used for pure functions of other signals, ie: s.Memo(s => s.D(A) + s.D(B))
    /// </summary>
    public sealed class Memo<T> : Memo, ISignal<T>, Trigger.Friend {
        Trigger<T> trigger = new Trigger<T>();
        MemoBlock<T> selector;
        MemoFoldBlock<T> fold;
        T value;

        /// <summary>The current value of the memo, brought up to date with its dependencies first</summary>
        public T Now {
            get {
                EnsureFresh(rethrowFault: true);
                return value;
            }
        }

        public Memo(string name, MemoBlock<T> selector, params ITrigger[] triggers) : base(name, triggers) {
            this.selector = selector;
            Start();
        }

        public Memo(string name, T seed, MemoFoldBlock<T> selector, params ITrigger[] triggers) : base(name, triggers) {
            value = seed;
            fold = selector;
            Start();
        }

        protected override bool Compute(MemoBuilder s) {
            T next;
            if (fold != null) next = fold(s, value);
            else if (selector != null) next = selector(s);
            else return false;
            if (EqualityComparer<T>.Default.Equals(next, value)) return false;
            value = next;
            return true;
        }

        protected override void Notify() {
            trigger.Invoke(value);
        }

        // Subscribing or depending runs the memo if it hasn't yet. Until it has run, it doesn't
        // know its dependencies, so it couldn't notice a change

        SpokeHandle Trigger.Friend.AddDependant(Action action) {
            EnsureFresh(rethrowFault: false);
            return (trigger as Trigger.Friend).AddDependant(action);
        }

        /// <summary>Subscribes to value changes, returns unsubscribe handle</summary>
        public SpokeHandle Subscribe(Action action) {
            EnsureFresh(rethrowFault: false);
            return trigger.Subscribe(action);
        }

        /// <summary>Subscribes to value changes, returns unsubscribe handle</summary>
        public SpokeHandle Subscribe(Action<T> action) {
            EnsureFresh(rethrowFault: false);
            return trigger.Subscribe(action);
        }

        /// <summary>Unsubscribe the action. Prefer SpokeHandle.Dispose() instead</summary>
        public void Unsubscribe(Action action)
            => trigger.Unsubscribe(action);

        /// <summary>Unsubscribe the action. Prefer SpokeHandle.Dispose() instead</summary>
        public void Unsubscribe(Action<T> action)
            => trigger.Unsubscribe(action);
    }

    /// <summary>
    /// DSL-style builder passed into MemoBlock
    /// It's minimal compared to EffectBuilder, since memos are intended to be pure functions
    /// </summary>
    public struct MemoBuilder {
        Memo memo;

        internal MemoBuilder(Memo memo) {
            this.memo = memo;
        }

        /// <summary>
        /// Dynamically adds a signal dependency to the memo, and returns its current value
        /// The memo will recompute when the signal changes value
        /// </summary>
        public U D<U>(ISignal<U> signal) {
            // Read before subscribing. Reading a memo may bring it up to date, and that change is
            // already in the value read here. It shouldn't count as a change since
            var now = signal.Now;
            (memo as Memo.Friend).AddDynamicTrigger(signal);
            return now;
        }

        /// <summary>Attach a cleanup function, executed before the memo recomputes or is disposed</summary>
        public void OnCleanup(Action fn)
            => (memo as Memo.Friend).OnCleanup(fn);
    }
}
