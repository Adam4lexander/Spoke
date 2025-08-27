using System.Collections.Generic;
using System;

namespace Spoke {
    
    internal sealed class OrderedWorkStack<T> where T : Epoch {

        List<T> incoming = new(); 
        HashSet<T> set = new(); 
        List<T> list = new(); 
        Comparison<T> comp;
        bool dirty;

        public OrderedWorkStack(Comparison<T> comp) {
            this.comp = comp;
        }

        public bool Has => Take(false);

        public void Enqueue(T t) {
            incoming.Add(t);
        }

        public T Peek() {
            return Take(true) ? list[^1] : default;
        }

        public T Pop() {
            if (!Take(true)) return default;

            var t = list[^1];
            list.RemoveAt(list.Count - 1);
            set.Remove(t);
            return t;
        }

        bool Take(bool sort) {

            if (incoming.Count > 0) {
                var startCount = list.Count;
                foreach (var t in incoming) {
                    if (!t.IsDetached && set.Add(t)) {
                        list.Add(t);
                    }
                }
                dirty = list.Count > startCount;
                incoming.Clear();
            }

            if (sort && dirty) {
                list.Sort(comp);
                dirty = false;
            }

            while (list.Count > 0 && list[^1].IsDetached) {
                set.Remove(list[^1]);
                list.RemoveAt(list.Count - 1);
            }

            return list.Count > 0;
        }
    }
}