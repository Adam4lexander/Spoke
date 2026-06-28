using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public interface IBody<T> : IDisposable {
        Circle Circle { get; set; }
        T Payload { get; }
        ReadOnlyList<IBody<T>> Overlaps { get; }
        ITrigger Changed { get; }
    }

    public class CollisionWorld<T> {

        class Body : IBody<T> {

            public T Payload { get; }
            public Circle Circle {
                get => circle;
                set {
                    if (value == circle) return;
                    world.Remove(this);
                    circle = value;
                    world.Insert(this);
                }
            }
            public ReadOnlyList<IBody<T>> Overlaps => new(overlaps);
            public ITrigger Changed => changed;

            readonly bool detects, detectable;
            readonly Trigger changed;
            readonly List<IBody<T>> overlaps;
            readonly List<(Body body, float dist2)> sorted;
            Circle circle;
            CollisionWorld<T> world;

            public Body(CollisionWorld<T> world, T payload, Circle circle, bool detects, bool detectable) {
                this.world = world;
                Payload = payload;
                this.circle = circle;
                this.detects = detects;
                this.detectable = detectable;
                if (detects) {
                    changed = Trigger.Create();
                    overlaps = new();
                    sorted = new();
                }
                world.Insert(this);
            }

            public void Dispose() {
                if (world == null) return;
                world.Remove(this);
                world = null;
            }

            // Rebuild the nearest-first overlap set and raise Changed if it shifted (detecting only).
            public void Recompute() {
                if (!detects) return;
                var area = circle;
                sorted.Clear();
                foreach (var body in world.Query(area))
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
        readonly List<(int x, int z)> cellBuffer = new();
        readonly float cellSize;
        readonly Action step;

        public CollisionWorld(float cellSize = 4f) {
            this.cellSize = cellSize;
            step = Step;
        }

        public IBody<T> Add(T payload, Circle circle, bool detects = false, bool detectable = true)
            => new Body(this, payload, circle, detects, detectable);

        public void Tick() => SpokeRuntime.Batch(step);

        void Step() {
            dirtyBodies.Clear();
            foreach (var cell in dirty)
                if (cells.TryGetValue(cell, out var list))
                    foreach (var body in list) dirtyBodies.Add(body);
            dirty.Clear();
            foreach (var body in dirtyBodies) body.Recompute();
        }

        HashSet<Body> Query(Circle c) {
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
