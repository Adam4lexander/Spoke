using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    [NonParallelizable]
    public class SpokeRuntimeTests : SpokeTestFixture {

        [Test]
        public void LambdaEpoch_InitBlock_RunsDuringTreeConstruction() {
            var initRan = 0;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                initRan++;
                return null;
            }));

            Assert.AreEqual(1, initRan, "Init block should run synchronously during tree construction");
        }

        [Test]
        public void TickBlock_RunsOnFlush() {
            var ticks = 0;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                return s => ticks++;
            }));

            tree.Flush();
            Assert.AreEqual(1, ticks);
        }

        [Test]
        public void OnCleanup_RunsOnDispose_InReverseOrder() {
            var order = new List<string>();

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.OnCleanup(() => order.Add("first"));
                s.OnCleanup(() => order.Add("second"));
                return null;
            }));

            tree.Dispose();

            CollectionAssert.AreEqual(new[] { "second", "first" }, order);
        }

        [Test]
        public void Batch_DefersFlushUntilDelegateCompletes() {
            var ticks = 0;
            EpochPorts ports = default;

            using var tree = SpokeTree.Spawn(new LambdaEpoch(s => {
                ports = s.Ports;
                return s => ticks++;
            }));
            Assert.AreEqual(1, ticks);

            SpokeRuntime.Batch(() => {
                ports.RequestTick();
                Assert.AreEqual(1, ticks, "Re-tick must not have run yet — Batch should hold the flush");
            });

            Assert.AreEqual(2, ticks);
        }

        [Test]
        public void Batch_NestedHolds_Stack() {
            var ticks = 0;
            EpochPorts ports = default;

            using var tree = SpokeTree.Spawn(new LambdaEpoch(s => {
                ports = s.Ports;
                return s => ticks++;
            }));
            Assert.AreEqual(1, ticks);

            SpokeRuntime.Batch(() => {
                SpokeRuntime.Batch(() => {
                    ports.RequestTick();
                });
                Assert.AreEqual(1, ticks, "Outer Batch should still hold even after inner Batch exits");
            });

            Assert.AreEqual(2, ticks);
        }

        [Test]
        public void AutoTrees_OrderedByCreation_WhenSameFlushLayer() {
            var log = new List<string>();
            SpokeTree<LambdaEpoch> t1 = null;
            SpokeTree<LambdaEpoch> t2 = null;

            SpokeRuntime.Batch(() => {
                t1 = SpokeTree.Spawn("T1", new LambdaEpoch(s => s => log.Add("T1")));
                t2 = SpokeTree.Spawn("T2", new LambdaEpoch(s => s => log.Add("T2")));
            });

            try {
                CollectionAssert.AreEqual(new[] { "T1", "T2" }, log);
            } finally {
                t1?.Dispose();
                t2?.Dispose();
            }
        }

        [Test]
        public void SpawnEager_OutranksSpawn_WhenSameBatch() {
            var log = new List<string>();
            SpokeTree<LambdaEpoch> dflt = null;
            SpokeTree<LambdaEpoch> eager = null;

            SpokeRuntime.Batch(() => {
                dflt = SpokeTree.Spawn("Default", new LambdaEpoch(s => s => log.Add("Default")));
                eager = SpokeTree.SpawnEager("Eager", new LambdaEpoch(s => s => log.Add("Eager")));
            });

            try {
                CollectionAssert.AreEqual(new[] { "Eager", "Default" }, log);
            } finally {
                dflt?.Dispose();
                eager?.Dispose();
            }
        }

        [Test]
        public void NestedTreeSpawn_DuringFlush_FlushesNestedInScope() {
            var log = new List<string>();
            SpokeTree<LambdaEpoch> inner = null;

            using var outer = SpokeTree.Spawn("Outer", new LambdaEpoch(s => s => {
                log.Add("Before");
                inner = SpokeTree.Spawn("Inner", new LambdaEpoch(s => s => log.Add("Inner")));
                log.Add("After");
            }));
            try {
                CollectionAssert.AreEqual(new[] { "Before", "Inner", "After" }, log);
            } finally {
                inner?.Dispose();
            }
        }

    }
}
