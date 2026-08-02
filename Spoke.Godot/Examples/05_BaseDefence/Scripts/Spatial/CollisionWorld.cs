using System;
using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

// A tiny collision system for circles, used instead of Godot's physics server.
//
// Godot's Area2D would work, but it would drag in collision layers and masks that a consuming
// project has to configure, and its overlap callbacks are deferred by a frame. The whole game only
// needs circle-circle overlap, so a spatial hash is both smaller and easier to reason about — and
// it gives overlaps as a *reactive* signal, which is the part that matters here.
//
// ------------------------------------------------------------------------------------------
//  var world = new CollisionWorld<Building>();          // owners are type T
//
//  // Collider: a detectable circle, bound to an owner. Re-sampled each tick.
//  var collider = world.AddCollider(building, () => new Circle(pos, radius));
//
//  // Sensor: detects colliders, but is itself undetectable.
//  var sensor = world.AddSensor(() => new Circle(pos, range));
//
//  // Overlaps: what it touches now, nearest-first.
//  foreach (var hit in sensor.Overlaps) hit.Owner.TakeDamage();
//
//  world.Tick();                                        // re-sample, refresh overlaps (once a frame)
//  var hits = world.Query(new Circle(pos, radius));     // one-off lookup
// ------------------------------------------------------------------------------------------

/// <summary>A circle that detects colliders overlapping it, but isn't detectable itself.</summary>
public interface ISensor<T> : IDisposable {
    /// <summary>The circle this sensor currently occupies.</summary>
    Circle Circle { get; }
    /// <summary>Colliders currently overlapping, sorted nearest-first.</summary>
    ReadOnlyList<ICollider<T>> Overlaps { get; }
    /// <summary>Fires when the set of Overlaps changes.</summary>
    ITrigger OverlapsChanged { get; }
}

/// <summary>A detectable circle, bound to the owner it stands in for.</summary>
public interface ICollider<T> : ISensor<T> {
    /// <summary>The object this collider represents.</summary>
    T Owner { get; }
}

/// <summary>A spatial hash of circles: add colliders and sensors, then Tick each frame to refresh overlaps.</summary>
public class CollisionWorld<T> {

    readonly Dictionary<(int x, int y), List<Body>> cells = new();
    readonly HashSet<(int x, int y)> dirty = new();
    readonly HashSet<Body> dirtyBodies = new();
    readonly HashSet<Body> queryBodies = new();
    readonly HashSet<Body> bodies = new();
    readonly List<(int x, int y)> cellBuffer = new();
    readonly float cellSize;
    readonly Action step;

    /// <summary>cellSize is the grid bucket size; set it near your typical query radius.</summary>
    public CollisionWorld(float cellSize = 128f) {
        this.cellSize = cellSize;
        step = Step;
    }

    /// <summary>Adds a query-only probe. getCircle is re-sampled each Tick; filter picks which owners it detects.</summary>
    public ISensor<T> AddSensor(Func<Circle> getCircle, Func<T, bool> filter = null)
        => new Body(this, default, getCircle, detectable: false, filter);

    /// <summary>Adds a detectable circle bound to owner, re-sampled from getCircle each Tick.</summary>
    public ICollider<T> AddCollider(T owner, Func<Circle> getCircle, Func<T, bool> filter = null)
        => new Body(this, owner, getCircle, detectable: true, filter);

    /// <summary>
    /// Syncs positions, recalculates overlaps, and fires OverlapsChanged where they changed.
    ///
    /// Wrapped in SpokeRuntime.Batch so the whole sweep lands as one flush: without it, each
    /// OverlapsChanged would flush the effects that depend on it before the next body is even
    /// re-sampled, and a turret could retarget against a half-updated world.
    /// </summary>
    public void Tick() => SpokeRuntime.Batch(step);

    /// <summary>One-off immediate lookup of colliders overlapping area.</summary>
    public List<ICollider<T>> Query(Circle area, List<ICollider<T>> storeIn = null) {
        storeIn ??= new List<ICollider<T>>();
        foreach (var body in Broadphase(area)) {
            if (body.detectable && area.Overlaps(body.Circle)) storeIn.Add(body);
        }
        return storeIn;
    }

    void Step() {
        foreach (var body in bodies) body.Poll();
        dirtyBodies.Clear();
        foreach (var cell in dirty) {
            if (cells.TryGetValue(cell, out var list)) {
                foreach (var body in list) dirtyBodies.Add(body);
            }
        }
        dirty.Clear();
        foreach (var body in dirtyBodies) body.Recompute();
    }

    HashSet<Body> Broadphase(Circle c) {
        queryBodies.Clear();
        foreach (var cell in Cells(c)) {
            if (cells.TryGetValue(cell, out var list)) {
                foreach (var body in list) queryBodies.Add(body);
            }
        }
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

    List<(int x, int y)> Cells(Circle c) {
        cellBuffer.Clear();
        var r = c.Radius;
        var minX = Mathf.FloorToInt((c.Center.X - r) / cellSize);
        var maxX = Mathf.FloorToInt((c.Center.X + r) / cellSize);
        var minY = Mathf.FloorToInt((c.Center.Y - r) / cellSize);
        var maxY = Mathf.FloorToInt((c.Center.Y + r) / cellSize);
        for (var x = minX; x <= maxX; x++) {
            for (var y = minY; y <= maxY; y++) cellBuffer.Add((x, y));
        }
        return cellBuffer;
    }

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
        readonly Func<T, bool> filter;
        Circle circle;
        CollisionWorld<T> world;

        public Body(CollisionWorld<T> world, T owner, Func<Circle> getCircle, bool detectable, Func<T, bool> filter) {
            this.world = world;
            Owner = owner;
            this.getCircle = getCircle;
            circle = getCircle();
            this.detectable = detectable;
            this.filter = filter;
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

        public void Poll() {
            var next = getCircle();
            if (next == circle) return;
            world.Remove(this);
            circle = next;
            world.Insert(this);
        }

        public void Recompute() {
            var area = circle;
            sorted.Clear();
            foreach (var body in world.Broadphase(area)) {
                if (body.detectable && body != this && (filter == null || filter(body.Owner)) && area.Overlaps(body.circle)) {
                    sorted.Add((body, body.circle.Center.DistanceSquaredTo(area.Center)));
                }
            }
            sorted.Sort((a, b) => a.dist2.CompareTo(b.dist2));
            if (Same()) return;
            overlaps.Clear();
            foreach (var s in sorted) overlaps.Add(s.body);
            changed.Invoke();
        }

        bool Same() {
            if (overlaps.Count != sorted.Count) return false;
            for (var i = 0; i < overlaps.Count; i++) {
                if (!ReferenceEquals(overlaps[i], sorted[i].body)) return false;
            }
            return true;
        }
    }
}
