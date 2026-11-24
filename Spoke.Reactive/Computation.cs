using System;
using System.Collections.Generic;

namespace Spoke {

    /// <summary>
    /// Abstract base class for the reactive epochs: Effect, Phase and Reaction
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

    // Manages trigger subscriptions for a Computation or a Memo
    // Dependencies are matched to slots by position, so a run that reads the same dependencies in
    // the same order rebinds nothing. Reading one twice simply takes two slots
    internal class DependencyTracker : IDisposable {
        Action schedule;
        // Optional. Makes a second subscription to a dependency, held for as long as the first
        Func<ITrigger, SpokeHandle> link;
        List<Slot> staticSlots = new List<Slot>();
        List<Slot> dynamicSlots = new List<Slot>();
        List<Action> slotCallbacks = new List<Action>();
        int depIndex;

        public DependencyTracker(Action schedule, Func<ITrigger, SpokeHandle> link = null) {
            this.schedule = schedule;
            this.link = link;
        }

        // The dependencies in the order they were read: the static ones, then the last run's dynamic ones
        public int Count => staticSlots.Count + dynamicSlots.Count;

        public ITrigger this[int index] => index < staticSlots.Count
            ? staticSlots[index].Trigger
            : dynamicSlots[index - staticSlots.Count].Trigger;

        public void AddStatic(ITrigger trigger) {
            staticSlots.Add(Bind(trigger, schedule));
        }

        public void BeginDynamic() {
            depIndex = 0;
        }

        public void AddDynamic(ITrigger trigger) {
            if (depIndex >= dynamicSlots.Count) {
                dynamicSlots.Add(Bind(trigger, ScheduleFromIndex(depIndex)));
            } else if (dynamicSlots[depIndex].Trigger != trigger) {
                dynamicSlots[depIndex].Unbind();
                dynamicSlots[depIndex] = Bind(trigger, ScheduleFromIndex(depIndex));
            }
            depIndex++;
        }

        public void EndDynamic() {
            while (dynamicSlots.Count > depIndex) {
                dynamicSlots[dynamicSlots.Count - 1].Unbind();
                dynamicSlots.RemoveAt(dynamicSlots.Count - 1);
            }
        }

        public void Dispose() {
            foreach (var slot in staticSlots) slot.Unbind();
            foreach (var slot in dynamicSlots) slot.Unbind();
            staticSlots.Clear(); dynamicSlots.Clear();
        }

        // Spoke's own triggers and signals tell their dependants before they call any subscriber.
        // Anything else that implements ITrigger can only be subscribed to
        Slot Bind(ITrigger trigger, Action action) {
            return new Slot {
                Trigger = trigger,
                Handle = trigger is Trigger.Friend friend ? friend.AddDependant(action) : trigger.Subscribe(action),
                Link = link != null ? link(trigger) : default
            };
        }

        struct Slot {
            public ITrigger Trigger;
            public SpokeHandle Handle;
            public SpokeHandle Link;

            public void Unbind() {
                Handle.Dispose();
                Link.Dispose();
            }
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