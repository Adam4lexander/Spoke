using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class PhaseTests : SpokeTestFixture {

        [Test]
        public void Phase_MountsAndUnmountsOnBoolFlip() {
            var gate = State.Create(false);
            var mounted = 0;
            var cleaned = 0;

            using var tree = SpokeTree.Spawn(new Phase("p", gate, s => {
                mounted++;
                s.OnCleanup(() => cleaned++);
            }));
            Assert.AreEqual(0, mounted, "Phase should not mount while gate=false");

            gate.Set(true);
            Assert.AreEqual(1, mounted);
            Assert.AreEqual(0, cleaned);

            gate.Set(false);
            Assert.AreEqual(1, mounted);
            Assert.AreEqual(1, cleaned);

            gate.Set(true);
            Assert.AreEqual(2, mounted);
            Assert.AreEqual(1, cleaned);
        }

        [Test]
        public void Phase_ReRunsWhileMounted_WhenAdditionalTriggersFire() {
            var gate = State.Create(true);
            var pulse = Trigger.Create();
            var runs = 0;
            var cleanups = 0;

            using var tree = SpokeTree.Spawn(new Phase("p", gate, s => {
                runs++;
                s.OnCleanup(() => cleanups++);
            }, pulse));
            Assert.AreEqual(1, runs);

            pulse.Invoke();
            Assert.AreEqual(2, runs);
            Assert.AreEqual(1, cleanups);

            pulse.Invoke();
            Assert.AreEqual(3, runs);
            Assert.AreEqual(2, cleanups);
        }

        [Test]
        public void Phase_AdditionalTriggers_DoNotMount_WhenGateIsFalse() {
            var gate = State.Create(false);
            var pulse = Trigger.Create();
            var runs = 0;

            using var tree = SpokeTree.Spawn(new Phase("p", gate, s => runs++, pulse));

            pulse.Invoke();
            Assert.AreEqual(0, runs, "Triggers should not cause Phase block to run while gate=false");

            gate.Set(true);
            Assert.AreEqual(1, runs);
        }
    }
}
