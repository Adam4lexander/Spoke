using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    [NonParallelizable]
    public class SpokeTreeTests : SpokeTestFixture {

        [Test]
        public void Flush_ThrowsWhenAutoMode() {
            using var tree = SpokeTree.Spawn(new LambdaEpoch(s => null));
            Assert.Throws<Exception>(() => tree.Flush());
        }

        [Test]
        public void Tick_ThrowsWhenAutoMode() {
            using var tree = SpokeTree.Spawn(new LambdaEpoch(s => null));
            Assert.Throws<Exception>(() => tree.Tick());
        }

        [Test]
        public void Flush_ThrowsOnReentrancy() {
            SpokeTree<LambdaEpoch> capturedTree = null;
            bool? reentryThrew = null;

            capturedTree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                return s => {
                    try {
                        capturedTree.Flush();
                        reentryThrew = false;
                    } catch (Exception) {
                        reentryThrew = true;
                    }
                };
            }));
            try {
                capturedTree.Flush();
                Assert.IsTrue(reentryThrew.HasValue && reentryThrew.Value,
                    "Re-entrant Flush should throw");
            } finally {
                capturedTree.Dispose();
            }
        }

        [Test]
        public void Tick_AdvancesExactlyOnePending_FlushDrainsRest() {
            var ticked = new List<int>();

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Call(new LambdaEpoch(s => s => ticked.Add(1)));
                s.Call(new LambdaEpoch(s => s => ticked.Add(2)));
                s.Call(new LambdaEpoch(s => s => ticked.Add(3)));
                return null;
            }));
            try {
                tree.Tick();
                CollectionAssert.AreEqual(new[] { 1 }, ticked);

                tree.Flush();
                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ticked);
            } finally {
                tree.Dispose();
            }
        }

        [Test]
        public void Flush_DrainsAllPending() {
            var ticked = new List<int>();

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Call(new LambdaEpoch(s => s => ticked.Add(1)));
                s.Call(new LambdaEpoch(s => s => ticked.Add(2)));
                s.Call(new LambdaEpoch(s => s => ticked.Add(3)));
                return null;
            }));

            tree.Flush();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ticked);
        }

        [Test]
        public void Dispose_DetachesEntireSubtree() {
            var cleanups = new List<string>();

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.OnCleanup(() => cleanups.Add("root"));
                s.Call(new LambdaEpoch(s => {
                    s.OnCleanup(() => cleanups.Add("child"));
                    s.Call(new LambdaEpoch(s => {
                        s.OnCleanup(() => cleanups.Add("grand"));
                        return null;
                    }));
                    return null;
                }));
                return null;
            }));

            tree.Dispose();

            CollectionAssert.AreEqual(new[] { "grand", "child", "root" }, cleanups);
        }

        [Test]
        public void AutoTree_FlushesSynchronously_WhenUserRequestsTick() {
            var ticks = 0;
            EpochPorts ports = default;

            using var tree = SpokeTree.Spawn(new LambdaEpoch(s => {
                ports = s.Ports;
                return s => ticks++;
            }));
            Assert.AreEqual(1, ticks);

            ports.RequestTick();

            Assert.AreEqual(2, ticks, "Requesting a tick from user code must synchronously flush the Auto tree");
        }

        [Test]
        public void OscillationGuard_FaultsTheTree_WhenEpochsPingPong() {
            Errors.ExpectErrors();
            EpochPorts p1 = default, p2 = default;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Call(new LambdaEpoch(s => {
                    p1 = s.Ports;
                    return s => p2.RequestTick(); // e1 re-arms e2 on every tick
                }));
                s.Call(new LambdaEpoch(s => {
                    p2 = s.Ports;
                    return s => p1.RequestTick(); // e2 re-arms e1 on every tick
                }));
                return null;
            }));

            tree.Flush();

            Assert.IsNotNull(tree.Fault, "Tree should be faulted by oscillation guard");
        }

        [Test]
        public void Dispose_DuringFlush_IsDeferredUntilFlushCompletes() {
            // Disposing a tree from inside its own flush must NOT tear it down mid-tick — that would
            // pull the rug out from under the executing epoch. SpokeTree.Dispose defers the detach until
            // the live flush frame pops, so the current unit of work finishes first, then teardown runs.
            var log = new List<string>();
            SpokeTree tree = null;

            tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.OnCleanup(() => log.Add("cleanup"));
                return s => {
                    log.Add("before-dispose");
                    tree.Dispose();   // mid-flush: detach must be deferred, not immediate
                    log.Add("after-dispose"); // proves this tick wasn't torn down underneath us
                };
            }));

            tree.Flush();

            CollectionAssert.AreEqual(new[] { "before-dispose", "after-dispose", "cleanup" }, log,
                "Dispose during a flush must defer teardown until the current flush completes");
        }
    }
}
