using System;
using System.Collections.Generic;

namespace Spoke {

    /// <summary>
    /// A struct that wraps a List<T> and provides a read-only interface.
    /// It can be used in foreach loops without allocating.
    /// Read-only, not immutable: the owner may still mutate the wrapped list.
    /// Two wrappers are equal when they hold the same list and the same version.
    /// </summary>
    public readonly struct ReadOnlyList<T> : IEquatable<ReadOnlyList<T>> {
        static readonly List<T> empty = new List<T>();

        readonly List<T> list;
        readonly long version;

        public ReadOnlyList(List<T> list) : this(list, 0) { }

        /// <summary>
        /// An owner that mutates the wrapped list should bump version each time it re-wraps,
        /// so the new wrapper compares unequal to wrappers of earlier generations
        /// </summary>
        public ReadOnlyList(List<T> list, long version) {
            this.list = list;
            this.version = version;
        }

        public List<T>.Enumerator GetEnumerator() => (list ?? empty).GetEnumerator();

        public int Count => list?.Count ?? 0;

        public T this[int index] => (list ?? empty)[index];

        public bool Contains(T item) => (list ?? empty).Contains(item);

        /// <summary>Copies the current contents into storeIn (cleared first; allocated if null). A stable snapshot of a possibly-live view</summary>
        public List<T> ToList(List<T> storeIn = null) {
            if (storeIn == null) storeIn = new List<T>(Count);
            else storeIn.Clear();
            for (var i = 0; i < Count; i++) storeIn.Add(list[i]);
            return storeIn;
        }

        public bool Equals(ReadOnlyList<T> other) => ReferenceEquals(list, other.list) && version == other.version;
        public override bool Equals(object obj) => obj is ReadOnlyList<T> other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(list, version);
    }
}
