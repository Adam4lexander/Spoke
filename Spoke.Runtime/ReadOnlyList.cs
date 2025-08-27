using System.Collections.Generic;

namespace Spoke {

    /// <summary>
    /// A struct that wraps a List<T> and provides a read-only interface.
    /// It can be used in foreach loops without allocating.
    /// </summary>
    public readonly struct ReadOnlyList<T> {
        readonly List<T> list;

        public ReadOnlyList(List<T> list) { 
            this.list = list; 
        }

        public List<T>.Enumerator GetEnumerator() => list.GetEnumerator();

        public int Count => list?.Count ?? 0;

        public T this[int index] => list[index];
    }
}