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
    }
}
