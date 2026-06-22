using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    [NonParallelizable]
    public class SpokeRuntimeTests : SpokeTestFixture {

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

        [Test]
        public void SpawnEager_EnablesWriteThenRead_ViaNestedFlush() {
            // SpawnEager gives a tree a higher-priority flush layer. When a lower-priority (default) tree
            // mutates a signal the eager tree depends on, the eager tree flushes NESTED — synchronously,
            // before the default tree's flush continues. That lets the default tree write a signal and
            // then read a value the eager tree derives from it, fresh, in the same flush ("write-then-read").
            // Were both trees the same priority, the derived read would be stale (0 here, not 10).
            var src = State.Create(0);
            var derived = State.Create(0);
            var go = State.Create(false);
            var observed = -1;

            // Eager tree keeps `derived` == src * 2.
            var eager = SpokeTree.SpawnEager(new Effect("derive", s => derived.Set(s.D(src) * 2)));
            // Default tree: on `go`, writes src and immediately reads the eager-derived value.
            var dflt = SpokeTree.Spawn(new Effect("writer", s => {
                if (s.D(go)) {
                    src.Set(5);
                    observed = derived.Now;
                }
            }));
            try {
                go.Set(true);
                Assert.AreEqual(10, observed,
                    "Eager tree must flush nested when src changes, so the default tree reads the fresh derived value");
            } finally {
                dflt.Dispose();
                eager.Dispose();
            }
        }

    }
}
