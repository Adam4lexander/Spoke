using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class ReactionTests : SpokeTestFixture {

        [Test]
        public void Reaction_DoesNotRunOnInitialMount() {
            var runs = 0;
            var trigger = Trigger.Create();

            using var tree = SpokeTree.SpawnManual(new Reaction("r", s => runs++, trigger));
            tree.Flush();

            Assert.AreEqual(0, runs, "Reaction should skip its initial mount");
        }

        [Test]
        public void Reaction_RunsOnTriggerFire() {
            var runs = 0;
            var trigger = Trigger.Create();

            using var tree = SpokeTree.SpawnManual(new Reaction("r", s => runs++, trigger));
            tree.Flush();
            Assert.AreEqual(0, runs);

            trigger.Invoke();
            tree.Flush();
            Assert.AreEqual(1, runs);

            trigger.Invoke();
            tree.Flush();
            Assert.AreEqual(2, runs);
        }
    }
}
