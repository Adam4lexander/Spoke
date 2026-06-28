using System;
using System.Collections.Generic;

namespace Spoke.Examples.BaseDefence {

    // Something the world can detect. Move via Circle; drop with Dispose.
    public interface ICollider<T> : IDisposable {
        Circle Circle { get; set; }
        T Payload { get; }
    }

    // A detector. Overlaps lists the colliders it covers, nearest first; Changed (a Spoke trigger)
    // fires when that set changes. Move via Circle; drop with Dispose.
    public interface ISensor<T> : IDisposable {
        Circle Circle { get; set; }
        ReadOnlyList<ICollider<T>> Overlaps { get; }
        ITrigger Changed { get; }
    }

    // A stand-in for Unity physics: circles on the XZ plane.
    public class CollisionWorld<T> {

        const float CellSize = 4f;

        // A circle living in the hash: registers on construct, re-buckets when moved, leaves on Dispose.
        abstract class Body {
            protected CollisionWorld<T> world;
            Circle circle;
            protected Body(CollisionWorld<T> world, Circle circle) {
                this.world = world;
                this.circle = circle;
                world.hash.Insert(this, circle.Bounds);
            }
            public Circle Circle {
                get => circle;
                set { if (value == circle) return; circle = value; world.hash.Move(this, value.Bounds); }
            }
            public void Dispose() {
                if (world == null) return;
                world.hash.Remove(this);
                world = null;
            }
        }

        class Collider : Body, ICollider<T> {
            public T Payload { get; }
            public Collider(CollisionWorld<T> world, T payload, Circle circle) : base(world, circle) => Payload = payload;
        }

        class Sensor : Body, ISensor<T> {

            public ReadOnlyList<ICollider<T>> Overlaps => new(current);
            public ITrigger Changed => changed;

            readonly Trigger changed = Trigger.Create();
            readonly List<ICollider<T>> current = new();
            readonly List<(ICollider<T> collider, float dist2)> sorted = new();

            public Sensor(CollisionWorld<T> world, Circle area) : base(world, area) { }

            // Rebuild the nearest-first overlap set from nearby bodies; true if it changed.
            public bool Recompute(List<Body> candidates) {
                var area = Circle;
                sorted.Clear();
                foreach (var body in candidates)
                    if (body is Collider c && area.Overlaps(c.Circle))
                        sorted.Add((c, (c.Circle.Center - area.Center).sqrMagnitude));
                sorted.Sort((a, b) => a.dist2.CompareTo(b.dist2));
                if (Unchanged()) return false;
                current.Clear();
                foreach (var item in sorted) current.Add(item.collider);
                return true;
            }

            public void FireChanged() => changed.Invoke();

            bool Unchanged() {
                if (current.Count != sorted.Count) return false;
                for (var i = 0; i < current.Count; i++)
                    if (!ReferenceEquals(current[i], sorted[i].collider)) return false;
                return true;
            }
        }

        readonly SpatialHash<Body> hash = new(CellSize);
        readonly List<Body> dirtyBodies = new();
        readonly List<Body> candidates = new();
        readonly List<Sensor> changedThisTick = new();

        public ICollider<T> AddCollider(T payload, Circle circle) => new Collider(this, payload, circle);
        public ISensor<T> AddSensor(Circle area) => new Sensor(this, area);

        // Recompute every sensor near a change, then notify — so observers see a settled world.
        public void Tick() {
            changedThisTick.Clear();
            hash.CollectDirty(dirtyBodies);
            foreach (var body in dirtyBodies)
                if (body is Sensor sensor) {
                    hash.Query(sensor.Circle.Bounds, candidates);
                    if (sensor.Recompute(candidates)) changedThisTick.Add(sensor);
                }
            foreach (var sensor in changedThisTick) sensor.FireChanged();
        }
    }
}
