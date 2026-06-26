using System;
using System.Collections.Generic;

namespace Spoke.Examples.BaseDefence {

    public class SpatialIndex<T> {

        public readonly struct Entry {

            public readonly T RefObject;
            public readonly Circle Circle;

            public Entry(T refObject, Circle circle) {
                RefObject = refObject;
                Circle = circle;
            }
        }

        public readonly struct Handle : IDisposable {

            readonly Action<long, Circle> update;
            readonly Action<long> remove;
            readonly long id;

            internal Handle(Action<long, Circle> update, Action<long> remove, long id) {
                this.update = update;
                this.remove = remove;
                this.id = id;
            }

            public void Update(Circle circle) => update?.Invoke(id, circle);

            public void Dispose() => remove?.Invoke(id);
        }

        public class Watcher : IDisposable {

            public readonly Circle Area;
            SpatialIndex<T> owner;
            readonly State<List<Entry>> items = State.Create(new List<Entry>());

            internal Watcher(SpatialIndex<T> owner, Circle area) {
                this.owner = owner;
                this.Area = area;
            }

            public ISignal<List<Entry>> Items => items;

            internal void Refresh() {
                if (owner == null) return;
                items.Set(owner.Query(Area));
            }

            public void Dispose() {
                owner?.watchers.Remove(this);
                owner = null;
            }
        }

        Action<long, Circle> _update;
        Action<long> _remove;
        readonly Dictionary<long, Entry> items = new();
        readonly List<(Entry entry, float dist2)> sortBuffer = new();
        readonly List<Watcher> watchers = new();
        long nextId;

        public SpatialIndex() {
            _update = Update;
            _remove = Remove;
        }

        public Handle Add(T refObject, Circle circle) {
            var id = nextId++;
            items[id] = new Entry(refObject, circle);
            NotifyWatchers(circle);
            return new Handle(_update, _remove, id);
        }

        void Update(long id, Circle circle) {
            if (items.TryGetValue(id, out var entry)) {
                items[id] = new Entry(entry.RefObject, circle);
                NotifyWatchers(entry.Circle, circle);
            }
        }

        void Remove(long id) {
            if (items.TryGetValue(id, out var entry)) {
                items.Remove(id);
                NotifyWatchers(entry.Circle);
            }
        }

        public Watcher Watch(Circle area) {
            var watcher = new Watcher(this, area);
            watchers.Add(watcher);
            watcher.Refresh();
            return watcher;
        }

        void NotifyWatchers(Circle changed, Circle? also = null) {
            foreach (var watcher in watchers) {
                if (watcher.Area.Overlaps(changed) || (also.HasValue && watcher.Area.Overlaps(also.Value))) {
                    watcher.Refresh();
                }
            }
        }

        public List<Entry> GetAll(List<Entry> storeIn = null) {
            storeIn = storeIn ?? new List<Entry>();
            storeIn.Clear();
            foreach (var kv in items) {
                storeIn.Add(kv.Value);
            }
            return storeIn;
        }

        public List<Entry> Query(Circle area, List<Entry> storeIn = null) {
            storeIn = storeIn ?? new List<Entry>();
            storeIn.Clear();
            sortBuffer.Clear();

            foreach (var kv in items) {
                var entry = kv.Value;
                if (area.Overlaps(entry.Circle)) {
                    var dist2 = (entry.Circle.Center - area.Center).sqrMagnitude;
                    sortBuffer.Add((entry, dist2));
                }
            }

            sortBuffer.Sort((a, b) => a.dist2.CompareTo(b.dist2));
            foreach (var item in sortBuffer) {
                storeIn.Add(item.entry);
            }
            return storeIn;
        }
    }
}
