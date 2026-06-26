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

        Action<long, Circle> _update;
        Action<long> _remove;
        readonly Dictionary<long, Entry> items = new();
        readonly List<(Entry entry, float dist2)> sortBuffer = new();
        long nextId;

        public SpatialIndex() {
            _update = Update;
            _remove = Remove;
        }

        public Handle Add(T refObject, Circle circle) {
            var id = nextId++;
            items[id] = new Entry(refObject, circle);
            return new Handle(_update, _remove, id);
        }

        void Update(long id, Circle circle) {
            if (items.TryGetValue(id, out var entry)) {
                items[id] = new Entry(entry.RefObject, circle);
            }
        }

        void Remove(long id) {
            items.Remove(id);
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
