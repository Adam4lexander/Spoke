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
    // frame to detect overlaps and notify every sensor.
    //
    // Colliders and sensors are both bodies in one SpatialHash — moving, adding, or removing either
    // marks the cells it touched dirty. A collider can only enter or leave a sensor's overlaps by
    // crossing a cell that sensor occupies, which dirties it; and a sensor that moves dirties its own
    // cells. So every sensor whose overlaps could have changed is sitting in a dirty cell. Tick()
    // therefore recomputes exactly the sensors collected from dirty cells — a long-lived sensor in a
    // quiet region does nothing. Unlike Unity it hands a sensor its full current overlap set (nearest
    // first), not just enter/exit deltas, and needs no layers — a separate world per category does.
    //
    // Colliders and sensors are handed out as ICollider<T>/ISensor<T>; their plumbing lives on
    // private nested classes, so nothing outside this world can drive them — not even same-assembly
    // code. The enclosing world keeps full access to those private members.
    public class CollisionWorld<T> {

        const float CellSize = 4f;

        abstract class Body {
            protected CollisionWorld<T> world;
            protected Body(CollisionWorld<T> world) => this.world = world;
            protected void Reposition(Circle shape) => world.hash.Move(this, shape.Bounds);
            public void Dispose() {
                if (world == null) return;
                world.hash.Remove(this);
                world = null;
            }
        }

        class Collider : Body, ICollider<T> {

            public Circle Circle {
                get => circle;
                set { if (value == circle) return; circle = value; Reposition(value); }
            }
            public T Payload { get; }

            Circle circle;

            public Collider(CollisionWorld<T> world, T payload, Circle circle) : base(world) {
                Payload = payload;
                this.circle = circle;
            }
        }

        class Sensor : Body, ISensor<T> {

            public Circle Area {
                get => area;
                set { if (value == area) return; area = value; Reposition(value); }
            }
            public ReadOnlyList<ICollider<T>> Overlaps => new(current);
            public ITrigger Changed => changed;

            Circle area;
            readonly Trigger changed = Trigger.Create();
            readonly List<ICollider<T>> current = new();
            readonly List<ICollider<T>> next = new();
            readonly List<(ICollider<T> collider, float dist2)> sortBuffer = new();

            public Sensor(CollisionWorld<T> world, Circle area) : base(world) {
                this.area = area;
            }

            public bool Recompute(List<Body> candidates) {
                sortBuffer.Clear();
                foreach (var body in candidates) {
                    if (body is Collider collider && area.Overlaps(collider.Circle)) {
                        sortBuffer.Add((collider, (collider.Circle.Center - area.Center).sqrMagnitude));
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

            static bool Same(List<ICollider<T>> a, List<ICollider<T>> b) {
                if (a.Count != b.Count) return false;
                for (var i = 0; i < a.Count; i++) {
                    if (!ReferenceEquals(a[i], b[i])) return false;
                }
                return true;
            }
        }

        readonly SpatialHash<Body> hash = new(CellSize);
        readonly List<Body> dirtyBodies = new();
        readonly List<Body> candidates = new();
        readonly List<Sensor> changedThisTick = new();

        public ICollider<T> AddCollider(T payload, Circle circle) {
            var collider = new Collider(this, payload, circle);
            hash.Insert(collider, circle.Bounds);
            return collider;
        }

        public ISensor<T> AddSensor(Circle area) {
            var sensor = new Sensor(this, area);
            hash.Insert(sensor, area.Bounds);
            return sensor;
        }

        // Detect first, notify second: every affected sensor's set settles before any Changed fires,
        // so observers always see a fully settled world — no mid-tick cascades. Only sensors collected
        // from dirty cells are touched; the rest keep last tick's overlaps untouched.
        public void Tick() {
            changedThisTick.Clear();
            hash.CollectDirty(dirtyBodies);
            foreach (var body in dirtyBodies) {
                if (body is Sensor sensor) {
                    hash.Query(sensor.Area.Bounds, candidates);
                    if (sensor.Recompute(candidates)) changedThisTick.Add(sensor);
                }
            }
            foreach (var sensor in changedThisTick) sensor.FireChanged();
        }
    }
}
