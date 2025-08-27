using System.Collections.Generic;
using System;

namespace Spoke {

    /// <summary>
    /// Determines the imperative ordering for a node in the call-tree. It's used to sort nodes by imperative
    /// execution order. This struct is the slow but robust fallback in case it doesn't fit into PackedTree128
    /// </summary>
    public struct TreeCoords : IComparable<TreeCoords> {
        List<long> coords;
        PackedTreeCoords128 packed;

        public long Tail => coords[^1];

        public TreeCoords Extend(long idx) {
            var next = new TreeCoords { 
                coords = new List<long>() 
            };

            if (coords != null) {
                next.coords.AddRange(coords);
            }

            next.coords.Add(idx);
            next.packed = PackedTreeCoords128.Pack(next.coords);
            return next;
        }

        public int CompareTo(TreeCoords other) {
            if (packed.IsValid && other.packed.IsValid) {
                return packed.CompareTo(other.packed);
            }
            
            var myDepth = coords?.Count ?? 0;
            var otherDepth = other.coords?.Count ?? 0;
            var minDepth = Math.Min(myDepth, otherDepth);
            for (int i = 0; i < minDepth; i++) {
                int cmp = coords[i].CompareTo(other.coords[i]);
                if (cmp != 0) return cmp;
            }

            return myDepth.CompareTo(otherDepth);
        }
    }

    /// <summary>
    /// Efficiently encodes up to 16 tree layers, with 256 nodes per layer. For Fast array sorting.
    /// </summary>
    public readonly struct PackedTreeCoords128 : IComparable<PackedTreeCoords128> {

        public static PackedTreeCoords128 Invalid => new(0, 0, byte.MaxValue);

        readonly ulong hi; // top 8 levels
        readonly ulong lo; // bottom 8 levels
        readonly byte depth;

        public PackedTreeCoords128(ulong hi, ulong lo, byte depth) { 
            this.hi = hi; 
            this.lo = lo; 
            this.depth = depth; 
        }
        
        public bool IsValid => depth < byte.MaxValue;

        public static PackedTreeCoords128 Pack(List<long> coords) {
            if (coords == null || coords.Count > 16) {
                return Invalid;
            }
            ulong hi = 0, lo = 0;
            for (int i = 0; i < coords.Count; i++) {
                var val = coords[i];
                if (val < 0 || val > 255) return Invalid;
                if (i < 8) hi |= ((ulong)val << ((7 - i) * 8));
                else lo |= ((ulong)val << ((15 - i) * 8));
            }

            return new PackedTreeCoords128(hi, lo, (byte)coords.Count);
        }

        public int CompareTo(PackedTreeCoords128 other) {
            int cmp = hi.CompareTo(other.hi);
            if (cmp != 0) {
                return cmp;
            }
            cmp = lo.CompareTo(other.lo);
            if (cmp != 0) {
                return cmp;
            }
            return depth.CompareTo(other.depth);
        }
    }
}