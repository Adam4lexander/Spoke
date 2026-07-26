using System;
using System.Collections.Generic;

namespace Spoke {

    /// <summary>
    /// Abstract base class for all reactive objects
    /// A Computation is a reactive action which runs when any of its triggers fire
    /// Supports both static and dynamic trigger subscriptions
    /// - Static triggers are subscribed once, when the computation is created
    /// - Dynamic triggers can be added during each run of the computation
    /// </summary>
    public abstract class Computation : Epoch {
        IEnumerable<ITrigger> triggers;
        DependencyTracker tracker;

        public Computation(string name, IEnumerable<ITrigger> triggers) {
            Name = name;
            this.triggers = triggers;
        }

        protected override TickBlock Init(EpochBuilder s) {
            tracker = s.Use(new DependencyTracker(s.Ports.RequestTick));
            foreach (var trigger in triggers) {
                tracker.AddStatic(trigger);
            }
            return s => {
                tracker.BeginDynamic();
                try { OnRun(s); } finally { tracker.EndDynamic(); }
            };
        }

        protected abstract void OnRun(EpochBuilder s);

        protected void AddStaticTrigger(ITrigger trigger) 
            => tracker.AddStatic(trigger);

        protected void AddDynamicTrigger(ITrigger trigger) 
            => tracker.AddDynamic(trigger);
    }

    // Manages dynamic trigger subscriptions for a Computation
    // Dependencies are matched to slots by position, so a run that reads the same dependencies in
    // the same order rebinds nothing. Reading one twice simply takes two slots
    internal class DependencyTracker : IDisposable {
        Action schedule;
        List<SpokeHandle> staticHandles = new List<SpokeHandle>();
        List<(ITrigger t, SpokeHandle h)> dynamicHandles = new List<(ITrigger t, SpokeHandle h)>();
        List<Action> slotCallbacks = new List<Action>();
        int depIndex;

        public DependencyTracker(Action schedule) {
            this.schedule = schedule;
        }

        public void AddStatic(ITrigger trigger) {
            staticHandles.Add(trigger.Subscribe(schedule));
        }

        public void BeginDynamic() {
            depIndex = 0;
        }

        public void AddDynamic(ITrigger trigger) {
            if (depIndex >= dynamicHandles.Count) {
                dynamicHandles.Add((trigger, trigger.Subscribe(ScheduleFromIndex(depIndex))));
            } else if (dynamicHandles[depIndex].t != trigger) {
                dynamicHandles[depIndex].h.Dispose();
                dynamicHandles[depIndex] = (trigger, trigger.Subscribe(ScheduleFromIndex(depIndex)));
            }
            depIndex++;
        }

        public void EndDynamic() {
            while (dynamicHandles.Count > depIndex) {
                dynamicHandles[dynamicHandles.Count - 1].h.Dispose();
                dynamicHandles.RemoveAt(dynamicHandles.Count - 1);
            }
        }

        public void Dispose() {
            foreach (var handle in staticHandles) handle.Dispose();
            foreach (var handle in dynamicHandles) handle.h.Dispose();
            staticHandles.Clear(); dynamicHandles.Clear();
        }

        // Dependencies may fire while we're in the middle of refreshing them
        // The callback captures the slot index at the time of subscription
        // The index tells us if the dependency is valid or stale when it fires
        // Callbacks are cached per slot, so rebinding a slot doesn't allocate
        Action ScheduleFromIndex(int index) {
            while (slotCallbacks.Count <= index) {
                var i = slotCallbacks.Count;
                slotCallbacks.Add(() => { if (i < depIndex) schedule(); });
            }
            return slotCallbacks[index];
        }
    }
}