using System;
using System.Collections.Generic;

namespace Spoke {

    public abstract class Computation : Epoch {
        IEnumerable<ITrigger> triggers;
        DependencyTracker tracker;

        public Computation(string name, IEnumerable<ITrigger> triggers) {
            Name = name;
            this.triggers = triggers;
        }

        protected override TickBlock Init(EpochBuilder s) {
            tracker = new DependencyTracker(s.Ports.RequestTick);
            s.OnCleanup(() => tracker.Dispose());
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

    internal class DependencyTracker : IDisposable {
        Action schedule;
        HashSet<ITrigger> seen = new HashSet<ITrigger>();
        List<(ITrigger t, SpokeHandle h)> staticHandles = new List<(ITrigger t, SpokeHandle h)>();
        List<(ITrigger t, SpokeHandle h)> dynamicHandles = new List<(ITrigger t, SpokeHandle h)>();
        public int depIndex;

        public DependencyTracker(Action schedule) {
            this.schedule = schedule;
        }

        public void AddStatic(ITrigger trigger) {
            if (!seen.Add(trigger)) return;
            staticHandles.Add((trigger, trigger.Subscribe(ScheduleFromIndex(-1))));
        }

        public void BeginDynamic() {
            depIndex = 0;
            seen.Clear();
            foreach (var dep in staticHandles) {
                seen.Add(dep.t);
            } 
        }

        public void AddDynamic(ITrigger trigger) {
            if (!seen.Add(trigger)) return;
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
            seen.Clear();
            foreach (var handle in staticHandles) handle.h.Dispose();
            foreach (var handle in dynamicHandles) handle.h.Dispose();
            staticHandles.Clear(); dynamicHandles.Clear();
        }

        Action ScheduleFromIndex(int index) 
            => () => { if (index < depIndex) schedule(); }; 
    }
}