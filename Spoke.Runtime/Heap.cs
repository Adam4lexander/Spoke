using System.Collections.Generic;
using System;

namespace Spoke {

    /// <summary>
    /// Provides a min-heap for arbitrary types, supporting insert, peek,
    /// and remove-min operations with caller-defined ordering.
    /// </summary>
    internal sealed class Heap<T> {

        public struct Item { 
            public long ID; 
            public T V; 
        }

        long currKey;
        List<Item> heap = new List<Item>();
        Comparison<T> comparison;

        public int Count => heap.Count;

        public Heap(Comparison<T> comparison) {
            this.comparison = comparison;
        }

        public long Insert(T value) {
            var item = new Item { ID = currKey++, V = value };
            heap.Add(item);
            HeapifyUp(heap.Count - 1);
            return item.ID;
        }

        public Item RemoveMin() {
            if (heap.Count == 0) throw new InvalidOperationException("Heap is empty");
            var min = heap[0];
            RemoveAt(0);
            return min;
        }

        public Item PeekMin() {
            if (heap.Count == 0) throw new InvalidOperationException("Heap is empty");
            return heap[0];
        }

        void RemoveAt(int index) {
            var lastIndex = heap.Count - 1;
            if (index != lastIndex) Swap(index, lastIndex);
            heap.RemoveAt(lastIndex);
            if (index < heap.Count) HeapifyDown(index);
        }

        void HeapifyUp(int index) {
            var item = heap[index];
            var parent = (index - 1) / 2;
            while (index > 0 && comparison(item.V, heap[parent].V) < 0) {
                heap[index] = heap[parent];
                index = parent;
                parent = (index - 1) / 2;
            }
            heap[index] = item;
        }

        void HeapifyDown(int index) {
            while (true) {
                var leftChild = 2 * index + 1;
                var rightChild = 2 * index + 2;
                var smallest = index;
                if (leftChild < heap.Count && comparison(heap[leftChild].V, heap[smallest].V) < 0)
                    smallest = leftChild;
                if (rightChild < heap.Count && comparison(heap[rightChild].V, heap[smallest].V) < 0)
                    smallest = rightChild;
                if (smallest == index)
                    break;
                Swap(index, smallest);
                index = smallest;
            }
        }

        void Swap(int i, int j) { 
            var tmp = heap[i]; 
            heap[i] = heap[j]; 
            heap[j] = tmp; 
        }
    }
}