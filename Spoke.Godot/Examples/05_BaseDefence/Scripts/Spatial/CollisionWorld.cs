using System;
using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

// A tiny collision system for circles, used instead of Godot's physics server.
//
// I avoided Godot physics so the example wouldn't depend on how collision layers are set up in
// the project it's imported into. The whole game only needs circle-circle overlap tests, so a
// custom collision engine was simple to write.
//
// ------------------------------------------------------------------------------------------
//  var world = new CollisionWorld<Building>();          // owners are type Building
//
//  // Collider: a detectable circle, bound to an owner. Re-sampled each tick.
//  var collider = world.AddCollider(building, () => new Circle(pos, radius));
//
//  // Sensor: detects colliders, but is itself undetectable.
//  var sensor = world.AddSensor(() => new Circle(pos, range));
//
//  // Overlaps: a signal of what it touches now, nearest-first.
//  foreach (var hit in sensor.Overlaps.Now) hit.Owner.TakeDamage();
//  s.Effect(s => { foreach (var hit in s.D(sensor.Overlaps)) ... });   // reactive read
//
//  world.Tick();                                        // re-sample, refresh overlaps (once a frame)
//  var hits = world.Query(new Circle(pos, radius));     // one-off lookup
// ------------------------------------------------------------------------------------------

/// <summary>A circle that detects colliders overlapping it, but isn't detectable itself.</summary>
public interface ISensor<T> : IDisposable {
    /// <summary>The circle this sensor currently occupies.</summary>
    Circle Circle { get; }
    /// <summary>A signal of the colliders currently overlapping, sorted nearest-first.</summary>
    ISignal<ReadOnlyList<ICollider<T>>> Overlaps { get; }
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

    /// <summary>Syncs collider/sensor positions, calculates overlaps, and publishes Overlaps where they changed</summary>
    // Batched so the whole sweep lands as one flush, rather than each Overlaps publish
    // flushing its effects against a half-updated world.
    public void Tick() => SpokeRuntime.Batch(step);

    /// <summary>One-off immediate lookup of colliders overlapping area, stored in storeIn (cleared first; allocated if null).</summary>
    public List<ICollider<T>> Query(Circle area, List<ICollider<T>> storeIn = null) {
        if (storeIn == null) storeIn = new List<ICollider<T>>();
        else storeIn.Clear();
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
        public ISignal<ReadOnlyList<ICollider<T>>> Overlaps => overlapsState;

        public readonly bool detectable;
        readonly State<ReadOnlyList<ICollider<T>>> overlapsState = new();
        readonly List<ICollider<T>> overlaps = new();
        long version;
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
            overlapsState.Set(new ReadOnlyList<ICollider<T>>(overlaps, ++version));
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
            // Same list every publish; the bumped version makes each generation compare unequal
            overlapsState.Set(new ReadOnlyList<ICollider<T>>(overlaps, ++version));
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
