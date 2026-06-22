using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class HeapTests : SpokeTestFixture {

        [Test]
        public void Insert_RemoveMin_YieldsAscendingOrder() {
            var heap = new Heap<int>((a, b) => a.CompareTo(b));
            foreach (var v in new[] { 5, 1, 3, 2, 4 }) heap.Insert(v);

            var actual = new List<int>();
            while (heap.Count > 0) actual.Add(heap.RemoveMin());

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, actual);
        }

        [Test]
        public void PeekMin_DoesNotRemove() {
            var heap = new Heap<int>((a, b) => a.CompareTo(b));
            heap.Insert(7);
            heap.Insert(2);

            Assert.AreEqual(2, heap.PeekMin());
            Assert.AreEqual(2, heap.Count);
        }

        [Test]
        public void PeekMin_OnEmpty_Throws() {
            var heap = new Heap<int>((a, b) => a.CompareTo(b));
            Assert.Throws<InvalidOperationException>(() => heap.PeekMin());
        }

        [Test]
        public void RemoveMin_OnEmpty_Throws() {
            var heap = new Heap<int>((a, b) => a.CompareTo(b));
            Assert.Throws<InvalidOperationException>(() => heap.RemoveMin());
        }

        [Test]
        public void Stress_RandomInserts_RemoveMinIsSorted() {
            var rng = new Random(42);
            var heap = new Heap<int>((a, b) => a.CompareTo(b));
            var inserted = new List<int>();
            for (int i = 0; i < 100; i++) {
                var v = rng.Next();
                heap.Insert(v);
                inserted.Add(v);
            }
            inserted.Sort();

            var drained = new List<int>();
            while (heap.Count > 0) drained.Add(heap.RemoveMin());
            CollectionAssert.AreEqual(inserted, drained);
        }
    }
}
