using System.Collections.Generic;

namespace Spoke {

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