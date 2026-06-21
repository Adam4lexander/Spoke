using System;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class SpokeHandleTests : SpokeTestFixture {

        [Test]
        public void Dispose_InvokesOnDispose() {
            long captured = -1;
            var handle = SpokeHandle.Of(42, id => captured = id);
            handle.Dispose();
            Assert.AreEqual(42, captured);
        }

        [Test]
        public void Dispose_OnDefault_IsSafe() {
            var handle = default(SpokeHandle);
            Assert.DoesNotThrow(() => handle.Dispose());
        }

        [Test]
        public void Equals_AndHashCode_AreConsistent() {
            Action<long> dispose = _ => { };
            var a = SpokeHandle.Of(7, dispose);
            var b = SpokeHandle.Of(7, dispose);
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equals_DifferentId_AreNotEqual() {
            Action<long> dispose = _ => { };
            var a = SpokeHandle.Of(7, dispose);
            var c = SpokeHandle.Of(8, dispose);
            Assert.IsFalse(a == c);
            Assert.IsTrue(a != c);
        }
    }
}
