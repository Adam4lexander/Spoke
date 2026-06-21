using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class StateTests : SpokeTestFixture {

        [Test]
        public void Set_SameValue_DoesNotNotify() {
            var state = State.Create(1);
            var calls = 0;
            using var sub = state.Subscribe(() => calls++);

            state.Set(1);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Set_DifferentValue_Notifies() {
            var state = State.Create(1);
            var calls = 0;
            using var sub = state.Subscribe(() => calls++);

            state.Set(2);
            state.Set(2);   // no change
            state.Set(3);

            Assert.AreEqual(2, calls);
        }

        [Test]
        public void Update_AppliesSetterToCurrentValue() {
            var state = State.Create(5);
            var observed = 0;
            using var sub = state.Subscribe((int v) => observed = v);

            state.Update(x => x * 2);

            Assert.AreEqual(10, state.Now);
            Assert.AreEqual(10, observed);
        }

        [Test]
        public void Update_NullSetter_IsNoOp() {
            var state = State.Create(5);
            var notified = false;
            using var sub = state.Subscribe(() => notified = true);

            state.Update(null);

            Assert.AreEqual(5, state.Now);
            Assert.IsFalse(notified);
        }

        [Test]
        public void Set_SameReference_DoesNotNotify() {
            var inst = new object();
            var state = State.Create(inst);
            var calls = 0;
            using var sub = state.Subscribe(() => calls++);

            state.Set(inst);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Set_DifferentReference_Notifies() {
            var state = State.Create(new object());
            var calls = 0;
            using var sub = state.Subscribe(() => calls++);

            state.Set(new object());

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Set_NullToNull_DoesNotNotify() {
            var state = State.Create<object>(null);
            var calls = 0;
            using var sub = state.Subscribe(() => calls++);

            state.Set(null);

            Assert.AreEqual(0, calls);
        }
    }
}
