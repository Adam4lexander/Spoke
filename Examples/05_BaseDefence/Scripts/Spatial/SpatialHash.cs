using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // A uniform spatial hash over axis-aligned bounds on the XZ plane. It knows nothing about what it
    // stores or about circles: an item is registered in every cell its bounds cover, and a query
    // returns the items whose cells intersect the query bounds. Two regions can overlap only if they
    // share a cell, so a bounds query never misses a real overlap — for any size. cellSize is purely
    // a performance knob (smaller = finer pruning but more cells per large item); correctness never
    // depends on it.
    //
    // Items are keyed by identity: Insert each once, then Move/Remove by the same reference. Queries
    // are clamped to the populated extent, so an unbounded (e.g. infinite) query region is still
    // finite work — it simply returns everything stored.
    //
    // It also tracks a dirty set: every cell touched by Insert/Move/Remove. CollectDirty returns the
    // items occupying those cells — everything whose overlaps may have changed — and drains the set,
    // so a caller can re-evaluate just what moved rather than sweeping the whole world.
    public class SpatialHash<T> {

        readonly Dictionary<(int x, int z), List<T>> cells = new();
        readonly Dictionary<T, Bounds> bounds = new();   // each item's current bounds — finds its cells on Move/Remove
        readonly HashSet<(int x, int z)> dirty = new();
        readonly HashSet<T> seen = new();   // dedup for Query (an item spans multiple cells)
        readonly List<(int x, int z)> cellBuffer = new();   // reusable scratch for cell enumeration
        readonly float cellSize;
        // Populated world extent on XZ (expand-only). Lets us clamp an unbounded query to finite work.
        // Inserted items are expected to be finite; only queries may be unbounded.
        float occMinX = float.PositiveInfinity, occMinZ = float.PositiveInfinity;
        float occMaxX = float.NegativeInfinity, occMaxZ = float.NegativeInfinity;

        public SpatialHash(float cellSize) { this.cellSize = cellSize; }

        public void Insert(T item, Bounds b) { bounds[item] = b; AddToCells(item, b); }
        public void Move(T item, Bounds b) { RemoveFromCells(item, bounds[item]); bounds[item] = b; AddToCells(item, b); }
        public void Remove(T item) { RemoveFromCells(item, bounds[item]); bounds.Remove(item); }

        // Dedup'd candidates whose cells intersect region. The exact test (narrow-phase) is the
        // caller's job.
        public void Query(Bounds region, List<T> results) {
            results.Clear();
            seen.Clear();
            foreach (var cell in Cells(region))
                if (cells.TryGetValue(cell, out var list))
                    foreach (var item in list)
                        if (seen.Add(item)) results.Add(item);
        }

        // Every item occupying a dirty cell (deduped) — the complete set whose overlaps may have
        // changed since the last drain. Consumes the dirty set: once collected, those cells are clean
        // again, ready to accumulate the next round of changes.
        public void CollectDirty(List<T> into) {
            into.Clear();
            seen.Clear();
            foreach (var cell in dirty)
                if (cells.TryGetValue(cell, out var list))
                    foreach (var item in list)
                        if (seen.Add(item)) into.Add(item);
            dirty.Clear();
        }

        void AddToCells(T item, Bounds b) {
            occMinX = Mathf.Min(occMinX, b.min.x); occMinZ = Mathf.Min(occMinZ, b.min.z);
            occMaxX = Mathf.Max(occMaxX, b.max.x); occMaxZ = Mathf.Max(occMaxZ, b.max.z);   // expand before Cells, so the clamp is a no-op for b
            foreach (var cell in Cells(b)) {
                if (!cells.TryGetValue(cell, out var list)) cells[cell] = list = new List<T>();
                list.Add(item);
                dirty.Add(cell);
            }
        }

        void RemoveFromCells(T item, Bounds b) {
            foreach (var cell in Cells(b)) {
                if (cells.TryGetValue(cell, out var list)) list.Remove(item);
                dirty.Add(cell);
            }
        }

        // The cells covering region, clamped to the populated extent. For a stored item the clamp is a
        // no-op (the extent only ever grows, so it already contains the item); for an unbounded query
        // region it keeps the work finite. Empty if nothing is stored or the region misses all content.
        List<(int x, int z)> Cells(Bounds region) {
            if (occMinX > occMaxX) return Fill(0, 0, -1, -1);   // empty
            var loX = Mathf.Max(region.min.x, occMinX); var hiX = Mathf.Min(region.max.x, occMaxX);
            var loZ = Mathf.Max(region.min.z, occMinZ); var hiZ = Mathf.Min(region.max.z, occMaxZ);
            if (loX > hiX || loZ > hiZ) return Fill(0, 0, -1, -1);   // no overlap with content
            return Fill(FloorToCell(loX), FloorToCell(loZ), FloorToCell(hiX), FloorToCell(hiZ));
        }

        List<(int x, int z)> Fill(int minX, int minZ, int maxX, int maxZ) {
            cellBuffer.Clear();
            for (var x = minX; x <= maxX; x++)
                for (var z = minZ; z <= maxZ; z++)
                    cellBuffer.Add((x, z));
            return cellBuffer;
        }

        int FloorToCell(float world) => Mathf.FloorToInt(world / cellSize);
    }
}
