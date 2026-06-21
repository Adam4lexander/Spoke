using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class TickerTests : SpokeTestFixture {

        sealed class LambdaTicker : Ticker {
            readonly Func<TickerBuilder, Epoch> block;
            public LambdaTicker(string name, Func<TickerBuilder, Epoch> block) { Name = name; this.block = block; }
            public LambdaTicker(Func<TickerBuilder, Epoch> block) : this("LambdaTicker", block) { }
            protected override Epoch Bootstrap(TickerBuilder s) => block(s);
        }

        [Test]
        public void OnTick_DoingNothing_FaultsTree() {
            Errors.ExpectErrors();
            using var tree = SpokeTree.SpawnManual(new LambdaTicker(s => {
                s.OnTick(s => {
                    // Intentionally do nothing — neither TickNext nor Pause
                });
                return new LambdaEpoch(s => null);
            }));

            tree.Flush();
            Assert.IsNotNull(tree.Fault, "Ticker should throw when OnTick neither advances nor pauses");
        }

        [Test]
        public void Ticker_TicksPending_InTreeCoordOrder() {
            var ticked = new List<int>();

            using var tree = SpokeTree.SpawnManual(new LambdaTicker(s => {
                var ports = s.Ports;
                s.OnTick(s => {
                    while (ports.HasPending) s.TickNext();
                });
                return new LambdaEpoch(s => {
                    s.Call(new LambdaEpoch(s => s => ticked.Add(0)));
                    s.Call(new LambdaEpoch(s => s => ticked.Add(1)));
                    s.Call(new LambdaEpoch(s => s => ticked.Add(2)));
                    return null;
                });
            }));
            tree.Flush();

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, ticked);
        }

        [Test]
        public void Pause_StopsFurtherTicks_ResumeRestarts() {
            var ticks = 0;
            var pauseAfterDraining = false;
            EpochPorts child = default;
            TickerPorts ports = default;

            using var tree = SpokeTree.SpawnManual(new LambdaTicker(s => {
                ports = s.Ports;
                s.OnTick(s => {
                    ticks++;
                    while (ports.HasPending) s.TickNext();
                    if (pauseAfterDraining) ports.Pause();
                });
                return new LambdaEpoch(s => {
                    child = s.Ports;
                    return s => { };
                });
            }));
            tree.Flush();
            Assert.AreEqual(1, ticks);

            pauseAfterDraining = true;
            child.RequestTick();
            tree.Flush();
            Assert.AreEqual(2, ticks, "Ticker should fire once more to process the new pending and then pause");

            child.RequestTick();
            tree.Flush();
            Assert.AreEqual(2, ticks, "Paused ticker should not fire");

            ports.Resume();
            tree.Flush();
            Assert.Greater(ticks, 2, "Resume should let the ticker fire again");
        }

        [Test]
        public void FaultBoundaryTicker_ContainsException_TreeStaysHealthy() {
            Errors.ExpectErrors();
            var innerRan = 0;

            var boundary = new LambdaTicker(s => {
                var ports = s.Ports;
                s.OnTick(s => {
                    try {
                        while (ports.HasPending) s.TickNext();
                    } catch (SpokeException) {
                        ports.Pause();
                    }
                });
                return new LambdaEpoch(s => s => {
                    innerRan++;
                    throw new Exception("inner-explode");
                });
            });

            using var tree = SpokeTree.SpawnManual(boundary);
            tree.Flush();

            Assert.AreEqual(1, innerRan, "Inner epoch must have run (and thrown) before boundary caught");
            Assert.IsNull(tree.Fault, "Tree should not fault — boundary contained the exception");
            Assert.IsNull(boundary.Fault, "Boundary's own tickBlock didn't throw, only its inner epoch did");
        }
    }
}
