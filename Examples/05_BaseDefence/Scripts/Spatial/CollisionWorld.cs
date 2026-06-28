using System;
using System.Collections.Generic;

namespace Spoke.Examples.BaseDefence {

    // A thing that can be detected. Move it by assigning Circle; drop it with Dispose.
    public interface ICollider<T> : IDisposable {
        Circle Circle { get; set; }
        T Payload { get; }
    }

    // A query region. After each tick Overlaps holds the colliders it covers, nearest first, and
    // Changed fires whenever that set (membership or order) changed. Move it by assigning Area;
    // drop it with Dispose.
    public interface ISensor<T> : IDisposable {
        Circle Area { get; set; }
        ReadOnlyList<ICollider<T>> Overlaps { get; }
        ITrigger Changed { get; }
    }

    // A tiny, self-contained collision world of circles on the XZ plane. It stands in for Unity's
    // physics: register colliders and sensors once and move them in place, then Tick() once per
    // frame to detect overlaps and notify every sensor in a single pass. Unlike Unity it hands a
    // sensor its full current overlap set (nearest first), not just enter/exit deltas, and needs
    // no layers — a separate world per category replaces them.
    //
    // Colliders and sensors are handed out as ICollider<T>/ISensor<T>; their plumbing lives on
    // private nested classes, so nothing outside this world can drive them — not even same-assembly
    // code. The enclosing world keeps full access to those private members.
    public class CollisionWorld<T> {

        class Collider : ICollider<T> {

            public Circle Circle { get; set; }
            public T Payload { get; }
            public int index;
            CollisionWorld<T> world;

            public Collider(CollisionWorld<T> world, T payload, Circle circle, int index) {
                this.world = world;
                Payload = payload;
                Circle = circle;
                this.index = index;
            }

            public void Dispose() {
                if (world == null) return;
                world.RemoveCollider(this);
                world = null;
            }
        }

        class Sensor : ISensor<T> {

            public Circle Area { get; set; }
            public ReadOnlyList<ICollider<T>> Overlaps => new(current);
            public ITrigger Changed => changed;

            readonly Trigger changed = Trigger.Create();
            readonly List<ICollider<T>> current = new();
            readonly List<ICollider<T>> next = new();
            readonly List<(ICollider<T> collider, float dist2)> sortBuffer = new();
            public int index;
            CollisionWorld<T> world;

            public Sensor(CollisionWorld<T> world, Circle area, int index) {
                this.world = world;
                Area = area;
                this.index = index;
            }

            public bool Recompute(List<Collider> colliders) {
                sortBuffer.Clear();
                foreach (var collider in colliders) {
                    if (Area.Overlaps(collider.Circle)) {
                        sortBuffer.Add((collider, (collider.Circle.Center - Area.Center).sqrMagnitude));
                    }
                }
                sortBuffer.Sort((a, b) => a.dist2.CompareTo(b.dist2));
                next.Clear();
                foreach (var item in sortBuffer) next.Add(item.collider);
                if (Same(current, next)) return false;
                current.Clear();
                current.AddRange(next);
                return true;
            }

            public void FireChanged() => changed.Invoke();

            public void Dispose() {
                if (world == null) return;
                world.RemoveSensor(this);
                world = null;
            }

            static bool Same(List<ICollider<T>> a, List<ICollider<T>> b) {
                if (a.Count != b.Count) return false;
                for (var i = 0; i < a.Count; i++) {
                    if (!ReferenceEquals(a[i], b[i])) return false;
                }
                return true;
            }
        }

        readonly List<Collider> colliders = new();
        readonly List<Sensor> sensors = new();
        readonly List<Sensor> changedThisTick = new();

        public ICollider<T> AddCollider(T payload, Circle circle) {
            var collider = new Collider(this, payload, circle, colliders.Count);
            colliders.Add(collider);
            return collider;
        }

        public ISensor<T> AddSensor(Circle area) {
            var sensor = new Sensor(this, area, sensors.Count);
            sensors.Add(sensor);
            return sensor;
        }

        // Detect first, notify second: every sensor's set is recomputed before any Changed fires,
        // so observers always see a fully settled world — no mid-tick cascades.
        public void Tick() {
            changedThisTick.Clear();
            foreach (var sensor in sensors) {
                if (sensor.Recompute(colliders)) changedThisTick.Add(sensor);
            }
            foreach (var sensor in changedThisTick) sensor.FireChanged();
        }

        void RemoveCollider(Collider collider) {
            var last = colliders.Count - 1;
            var i = collider.index;
            colliders[i] = colliders[last];
            colliders[i].index = i;
            colliders.RemoveAt(last);
        }

        void RemoveSensor(Sensor sensor) {
            var last = sensors.Count - 1;
            var i = sensor.index;
            sensors[i] = sensors[last];
            sensors[i].index = i;
            sensors.RemoveAt(last);
        }
    }
}
