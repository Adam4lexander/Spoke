using System;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class FaultTests : SpokeTestFixture {

        [Test]
        public void InitException_WrapsInSpokeException_AndMarksFaulted() {
            Errors.ExpectErrors();
            var faulty = new LambdaEpoch(s => throw new Exception("init-boom"));

            using var tree = SpokeTree.SpawnManual(faulty);

            Assert.IsNotNull(faulty.Fault);
            Assert.IsInstanceOf<Exception>(faulty.Fault.InnerException);
        }

        [Test]
        public void TickException_WrapsInSpokeException_AndMarksFaulted() {
            Errors.ExpectErrors();
            var faulty = new LambdaEpoch(s => s => throw new Exception("tick-boom"));

            using var tree = SpokeTree.SpawnManual(faulty);
            tree.Flush();

            Assert.IsNotNull(faulty.Fault);
        }

        [Test]
        public void FaultedEpoch_StopsRequestingTicks() {
            Errors.ExpectErrors();
            var ticks = 0;
            EpochPorts ports = default;
            var faulty = new LambdaEpoch(s => {
                ports = s.Ports;
                return s => {
                    ticks++;
                    if (ticks == 1) throw new Exception("first-tick-only");
                };
            });

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Call(faulty);
                return null;
            }));
            tree.Flush();
            Assert.AreEqual(1, ticks);
            Assert.IsNotNull(faulty.Fault);

            ports.RequestTick();
            tree.Flush();
            Assert.AreEqual(1, ticks, "Faulted epoch should not re-tick");
        }

        [Test]
        public void Fault_PropagatesUpToSpokeTree() {
            Errors.ExpectErrors();
            var faulty = new LambdaEpoch(s => throw new Exception("boom"));

            using var tree = SpokeTree.SpawnManual(faulty);

            Assert.IsNotNull(tree.Fault, "Fault from descendant should propagate to SpokeTree");
        }

        [Test]
        public void SpokeException_CarriesStackSnapshot() {
            Errors.ExpectErrors();
            var faulty = new LambdaEpoch(s => throw new Exception("boom"));

            using var tree = SpokeTree.SpawnManual(faulty);

            Assert.IsNotNull(faulty.Fault);
            Assert.Greater(faulty.Fault.StackSnapshot.Count, 0,
                "Stack snapshot should contain frames captured at fault time");
        }
    }
}
