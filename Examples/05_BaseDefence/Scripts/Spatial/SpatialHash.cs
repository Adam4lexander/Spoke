using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Buckets items into grid cells by their bounds so a region query checks only nearby items, not
    // all of them. Plain spatial partitioning — nothing Spoke-specific; it just keeps the demo cheap.
    public class SpatialHash<T> {

        readonly Dictionary<(int x, int z), List<T>> cells = new();
        readonly Dictionary<T, Bounds> bounds = new();
        readonly HashSet<(int x, int z)> dirty = new();
        readonly HashSet<T> seen = new();
        readonly List<(int x, int z)> cellBuffer = new();
        readonly float cellSize;
        // Bounding box of everything stored; clamps an unbounded query down to finite work.
        float occMinX = float.PositiveInfinity, occMinZ = float.PositiveInfinity;
        float occMaxX = float.NegativeInfinity, occMaxZ = float.NegativeInfinity;

        public SpatialHash(float cellSize) { this.cellSize = cellSize; }

        public void Insert(T item, Bounds b) { bounds[item] = b; AddToCells(item, b); }
        public void Move(T item, Bounds b) { RemoveFromCells(item, bounds[item]); bounds[item] = b; AddToCells(item, b); }
        public void Remove(T item) { RemoveFromCells(item, bounds[item]); bounds.Remove(item); }

        // Items whose cells overlap region, deduped. The exact overlap test is the caller's job.
        public void Query(Bounds region, List<T> results) {
            results.Clear();
            seen.Clear();
            foreach (var cell in Cells(region))
                if (cells.TryGetValue(cell, out var list))
                    foreach (var item in list)
                        if (seen.Add(item)) results.Add(item);
        }

        // Items in any cell touched since the last call; drains the dirty set as it goes.
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
            occMaxX = Mathf.Max(occMaxX, b.max.x); occMaxZ = Mathf.Max(occMaxZ, b.max.z);   // grow extent first; Cells clamps to it
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

        // Cells covering region, clamped to what's stored so an unbounded region stays finite.
        List<(int x, int z)> Cells(Bounds region) {
            cellBuffer.Clear();
            var loX = Mathf.Max(region.min.x, occMinX); var hiX = Mathf.Min(region.max.x, occMaxX);
            var loZ = Mathf.Max(region.min.z, occMinZ); var hiZ = Mathf.Min(region.max.z, occMaxZ);
            if (loX > hiX || loZ > hiZ) return cellBuffer;
            for (var x = FloorToCell(loX); x <= FloorToCell(hiX); x++)
                for (var z = FloorToCell(loZ); z <= FloorToCell(hiZ); z++)
                    cellBuffer.Add((x, z));
            return cellBuffer;
        }

        int FloorToCell(float world) => Mathf.FloorToInt(world / cellSize);
    }
}
