using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class ReadOnlyListTests : SpokeTestFixture {

        [Test]
        public void Count_And_Indexer() {
            var inner = new List<int> { 10, 20, 30 };
            var rol = new ReadOnlyList<int>(inner);
            Assert.AreEqual(3, rol.Count);
            Assert.AreEqual(10, rol[0]);
            Assert.AreEqual(30, rol[2]);
        }

        [Test]
        public void Enumeration_YieldsAll() {
            var rol = new ReadOnlyList<int>(new List<int> { 1, 2, 3 });
            var collected = new List<int>();
            foreach (var v in rol) collected.Add(v);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, collected);
        }

        [Test]
        public void NullList_CountIsZero() {
            var rol = new ReadOnlyList<int>(null);
            Assert.AreEqual(0, rol.Count);
        }

        [Test]
        public void NullList_EnumeratesZeroTimes() {
            var iterations = 0;
            foreach (var _ in default(ReadOnlyList<int>)) iterations++;
            Assert.AreEqual(0, iterations);
        }

        [Test]
        public void NullList_Indexer_ThrowsOutOfRange() {
            var rol = default(ReadOnlyList<int>);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = rol[0]);
        }

        [Test]
        public void Contains_FindsItem() {
            var rol = new ReadOnlyList<int>(new List<int> { 1, 2, 3 });
            Assert.IsTrue(rol.Contains(2));
            Assert.IsFalse(rol.Contains(4));
        }

        [Test]
        public void NullList_ContainsIsFalse() {
            Assert.IsFalse(default(ReadOnlyList<int>).Contains(0));
        }

        [Test]
        public void ToList_CopiesContents() {
            var rol = new ReadOnlyList<int>(new List<int> { 1, 2, 3 });
            var copy = rol.ToList();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, copy);
        }

        [Test]
        public void ToList_ClearsStoreIn() {
            var rol = new ReadOnlyList<int>(new List<int> { 2, 3 });
            var storeIn = new List<int> { 1 };
            var result = rol.ToList(storeIn);
            Assert.AreSame(storeIn, result);
            CollectionAssert.AreEqual(new[] { 2, 3 }, result);
        }

        [Test]
        public void ToList_DefaultWrapper_ReturnsEmptyList() {
            var copy = default(ReadOnlyList<int>).ToList();
            Assert.IsEmpty(copy);
        }

        [Test]
        public void Equals_SameListSameVersion_AreEqual() {
            var inner = new List<int> { 1, 2 };
            Assert.IsTrue(new ReadOnlyList<int>(inner, 3).Equals(new ReadOnlyList<int>(inner, 3)));
        }

        [Test]
        public void Equals_SameListDifferentVersion_AreNotEqual() {
            var inner = new List<int> { 1, 2 };
            Assert.IsFalse(new ReadOnlyList<int>(inner, 0).Equals(new ReadOnlyList<int>(inner, 1)));
        }

        [Test]
        public void Equals_DifferentListsSameContents_AreNotEqual() {
            var a = new ReadOnlyList<int>(new List<int> { 1, 2 });
            var b = new ReadOnlyList<int>(new List<int> { 1, 2 });
            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void Equals_DefaultEqualsDefault() {
            Assert.IsTrue(default(ReadOnlyList<int>).Equals(default));
        }

        [Test]
        public void EqualityComparer_UsesVersionedEquality() {
            // The path State<ReadOnlyList<T>>.Set dedups through
            var inner = new List<int> { 1 };
            var comparer = EqualityComparer<ReadOnlyList<int>>.Default;
            Assert.IsTrue(comparer.Equals(new(inner, 5), new(inner, 5)));
            Assert.IsFalse(comparer.Equals(new(inner, 5), new(inner, 6)));
        }

        [Test]
        public void GetHashCode_EqualWrappers_HashEqual() {
            var inner = new List<int> { 1, 2 };
            Assert.AreEqual(
                new ReadOnlyList<int>(inner, 2).GetHashCode(),
                new ReadOnlyList<int>(inner, 2).GetHashCode());
        }
    }
}
