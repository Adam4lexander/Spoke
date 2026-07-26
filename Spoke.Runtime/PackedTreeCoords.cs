using System;

namespace Spoke {

    /// <summary>
    /// Coordinate in the epoch tree, used to sort epochs by imperative execution order.
    /// Efficiently encodes up to 16 tree layers, with 256 nodes per layer.
    /// Coordinates that don't fit are Invalid, and are compared by walking the epoch
    /// parent chains instead (see Epoch.CompareTo).
    /// </summary>
    public readonly struct PackedTreeCoords128 : IComparable<PackedTreeCoords128> {

        const int BitsPerIndex = 8;             // one byte per layer
        const int LayersPerWord = 64 / BitsPerIndex;

        /// <summary>Largest index a layer can encode. Extending past it yields Invalid.</summary>
        public const int MaxIndex = (1 << BitsPerIndex) - 1;

        /// <summary>Layers the coordinate can hold. Extending a full one yields Invalid.</summary>
        public const int MaxDepth = LayersPerWord * 2;  // hi and lo

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
            if (!IsValid || depth == MaxDepth || idx < 0 || idx > MaxIndex) {
                return Invalid;
            }
            // Layers fill each word from its most significant byte down, so that comparing the
            // words as integers compares the layers in order.
            if (depth < LayersPerWord) {
                return new(hi | ((ulong)idx << ((LayersPerWord - 1 - depth) * BitsPerIndex)), lo, (byte)(depth + 1));
            }
            return new(hi, lo | ((ulong)idx << ((MaxDepth - 1 - depth) * BitsPerIndex)), (byte)(depth + 1));
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
