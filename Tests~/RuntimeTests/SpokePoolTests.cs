using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class SpokePoolTests : SpokeTestFixture {

        [Test]
        public void Get_WhenEmpty_ReturnsNewInstance() {
            var pool = SpokePool<List<int>>.Create();
            var got = pool.Get();
            Assert.IsNotNull(got);
        }

        [Test]
        public void ReturnThenGet_ReturnsSameInstance() {
            var pool = SpokePool<List<int>>.Create();
            var first = pool.Get();
            pool.Return(first);
            var second = pool.Get();
            Assert.AreSame(first, second);
        }

        [Test]
        public void Return_InvokesResetAction() {
            var resets = 0;
            var pool = SpokePool<List<int>>.Create(l => { l.Clear(); resets++; });
            var bucket = pool.Get();
            bucket.Add(1);
            bucket.Add(2);
            pool.Return(bucket);
            Assert.AreEqual(1, resets);
            Assert.AreEqual(0, bucket.Count, "reset should have cleared the list");
        }
    }
}
