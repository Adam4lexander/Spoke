using System;

namespace Spoke {

    /// <summary>
    /// Coordinate in the epoch tree, used to sort epochs by imperative execution order.
    /// Efficiently encodes up to 16 tree layers, with 256 nodes per layer.
    /// Coordinates that don't fit are Invalid, and are compared by walking the epoch
    /// parent chains instead (see Epoch.CompareTo).
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

        /// <summary>The coordinate one layer deeper, at index idx. Invalid if it doesn't fit.</summary>
        public PackedTreeCoords128 Extend(long idx) {
            if (!IsValid || depth == 16 || idx < 0 || idx > 255) {
                return Invalid;
            }
            if (depth < 8) {
                return new(hi | ((ulong)idx << ((7 - depth) * 8)), lo, (byte)(depth + 1));
            }
            return new(hi, lo | ((ulong)idx << ((15 - depth) * 8)), (byte)(depth + 1));
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
