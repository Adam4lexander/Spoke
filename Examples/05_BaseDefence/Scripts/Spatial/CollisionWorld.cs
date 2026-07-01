using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public interface ISensor<T> : IDisposable {
        Circle Circle { get; }
        ReadOnlyList<ICollider<T>> Overlaps { get; }
        ITrigger OverlapsChanged { get; }
    }

    public interface ICollider<T> : ISensor<T> {
        T Owner { get; }
    }

    public class CollisionWorld<T> {

        class Body : ICollider<T> {

            public T Owner { get; }
            public Circle Circle => circle;
            public ReadOnlyList<ICollider<T>> Overlaps => new(overlaps);
            public ITrigger OverlapsChanged => changed;

            public readonly bool detectable;
            readonly Trigger changed = Trigger.Create();
            readonly List<ICollider<T>> overlaps = new();
            readonly List<(Body body, float dist2)> sorted = new();
            readonly Func<Circle> getCircle;
            Circle circle;
            CollisionWorld<T> world;

            public Body(CollisionWorld<T> world, T owner, Func<Circle> getCircle, bool detectable) {
                this.world = world;
                Owner = owner;
                this.getCircle = getCircle;
                this.circle = getCircle();
                this.detectable = detectable;
                world.bodies.Add(this);
                world.Insert(this);
            }

            public void Dispose() {
                if (world == null) return;
                world.bodies.Remove(this);
                world.Remove(this);
                overlaps.Clear();
                world = null;
            }

            // Re-sample the source circle and re-grid if it moved.
            public void Poll() {
                var next = getCircle();
                if (next == circle) return;
                world.Remove(this);
                circle = next;
                world.Insert(this);
            }

            // Rebuild the nearest-first overlap set and raise Changed if it shifted.
            public void Recompute() {
                var area = circle;
                sorted.Clear();
                foreach (var body in world.Broadphase(area))
                    if (body.detectable && body != this && area.Overlaps(body.circle))
                        sorted.Add((body, (body.circle.Center - area.Center).sqrMagnitude));
                sorted.Sort((a, b) => a.dist2.CompareTo(b.dist2));
                if (Same()) return;
                overlaps.Clear();
                foreach (var s in sorted) overlaps.Add(s.body);
                changed.Invoke();
            }

            bool Same() {
                if (overlaps.Count != sorted.Count) return false;
                for (var i = 0; i < overlaps.Count; i++)
                    if (!ReferenceEquals(overlaps[i], sorted[i].body)) return false;
                return true;
            }
        }

        readonly Dictionary<(int x, int z), List<Body>> cells = new();
        readonly HashSet<(int x, int z)> dirty = new();
        readonly HashSet<Body> dirtyBodies = new();
        readonly HashSet<Body> queryBodies = new();
        readonly HashSet<Body> bodies = new();
        readonly List<(int x, int z)> cellBuffer = new();
        readonly float cellSize;
        readonly Action step;

        public CollisionWorld(float cellSize = 4f) {
            this.cellSize = cellSize;
            step = Step;
        }

        public ISensor<T> AddSensor(Func<Circle> getCircle)
            => new Body(this, default, getCircle, detectable: false);

        public ICollider<T> AddCollider(T owner, Func<Circle> getCircle)
            => new Body(this, owner, getCircle, detectable: true);

        public void Tick() => SpokeRuntime.Batch(step);

        public List<ICollider<T>> Query(Circle area, List<ICollider<T>> storeIn = null) {
            storeIn = storeIn ?? new List<ICollider<T>>();
            foreach (var body in Broadphase(area))
                if (body.detectable && area.Overlaps(body.Circle))
                    storeIn.Add(body);
            return storeIn;
        }

        void Step() {
            foreach (var body in bodies) body.Poll();
            dirtyBodies.Clear();
            foreach (var cell in dirty)
                if (cells.TryGetValue(cell, out var list))
                    foreach (var body in list) dirtyBodies.Add(body);
            dirty.Clear();
            foreach (var body in dirtyBodies) body.Recompute();
        }

        HashSet<Body> Broadphase(Circle c) {
            queryBodies.Clear();
            foreach (var cell in Cells(c))
                if (cells.TryGetValue(cell, out var list))
                    foreach (var body in list) queryBodies.Add(body);
            return queryBodies;
        }

        void Insert(Body b) {
            foreach (var cell in Cells(b.Circle)) {
                if (!cells.TryGetValue(cell, out var list)) cells[cell] = list = new();
                list.Add(b);
                dirty.Add(cell);
            }
        }

        void Remove(Body b) {
            foreach (var cell in Cells(b.Circle)) {
                if (cells.TryGetValue(cell, out var list)) list.Remove(b);
                dirty.Add(cell);
            }
        }

        List<(int x, int z)> Cells(Circle c) {
            cellBuffer.Clear();
            var r = c.Radius;
            var minX = Mathf.FloorToInt((c.Center.x - r) / cellSize);
            var maxX = Mathf.FloorToInt((c.Center.x + r) / cellSize);
            var minZ = Mathf.FloorToInt((c.Center.z - r) / cellSize);
            var maxZ = Mathf.FloorToInt((c.Center.z + r) / cellSize);
            for (var x = minX; x <= maxX; x++)
                for (var z = minZ; z <= maxZ; z++)
                    cellBuffer.Add((x, z));
            return cellBuffer;
        }
    }
}
