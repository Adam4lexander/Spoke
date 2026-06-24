using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class EffectTests : SpokeTestFixture {

        [Test]
        public void Effect_RunsOnMount() {
            var runs = 0;

            using var tree = SpokeTree.Spawn(new Effect("test", s => runs++));

            Assert.AreEqual(1, runs);
        }

        [Test]
        public void Effect_ReRuns_WhenDynamicDependencyChanges() {
            var state = State.Create(0);
            var runs = 0;
            var lastSeen = -1;

            using var tree = SpokeTree.Spawn(new Effect("test", s => {
                runs++;
                lastSeen = s.D(state);
            }));
            Assert.AreEqual(1, runs);
            Assert.AreEqual(0, lastSeen);

            state.Set(7);
            Assert.AreEqual(2, runs);
            Assert.AreEqual(7, lastSeen);
        }

        [Test]
        public void Effect_Cleanup_RunsBeforeRerun() {
            var state = State.Create(0);
            var log = new List<string>();

            using var tree = SpokeTree.Spawn(new Effect("test", s => {
                var v = s.D(state);
                log.Add($"mount:{v}");
                s.OnCleanup(() => log.Add($"cleanup:{v}"));
            }));

            state.Set(1);

            CollectionAssert.AreEqual(new[] { "mount:0", "cleanup:0", "mount:1" }, log);
        }

        [Test]
        public void Effect_StaticTrigger_ReRunsOnFire() {
            var trigger = Trigger.Create();
            var runs = 0;

            using var tree = SpokeTree.Spawn(new Effect("t", s => runs++, trigger));
            Assert.AreEqual(1, runs);

            trigger.Invoke();
            Assert.AreEqual(2, runs);

            trigger.Invoke();
            Assert.AreEqual(3, runs);
        }

        [Test]
        public void Effect_DynamicDependencies_DroppedTriggerNoLongerReRuns() {
            var gate = State.Create(true);
            var extra = State.Create(0);
            var runs = 0;

            using var tree = SpokeTree.Spawn(new Effect("dyn", s => {
                runs++;
                if (s.D(gate)) {
                    _ = s.D(extra);
                }
            }));
            Assert.AreEqual(1, runs);

            extra.Set(1);
            Assert.AreEqual(2, runs);

            gate.Set(false);
            Assert.AreEqual(3, runs);

            extra.Set(2);
            Assert.AreEqual(3, runs);

            gate.Set(true);
            Assert.AreEqual(4, runs);
        }

        [Test]
        public void Effect_Rerun_DetachesPriorChildren() {
            var state = State.Create(0);
            var childCleanups = 0;

            using var tree = SpokeTree.Spawn(new Effect("parent", s => {
                _ = s.D(state);
                s.Effect(s => {
                    s.OnCleanup(() => childCleanups++);
                });
            }));
            Assert.AreEqual(0, childCleanups);

            state.Set(1);
            Assert.AreEqual(1, childCleanups);

            state.Set(2);
            Assert.AreEqual(2, childCleanups);
        }

        [Test]
        public void Effect_NestedEffects_RunInImperativeOrder() {
            var state = State.Create(0);
            var log = new List<string>();

            // Two sibling effects, composed under a root Effect that wires them up once.
            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                s.Effect(s => {
                    log.Add($"O1:{s.D(state)}");
                    s.OnCleanup(() => log.Add("O1clean"));
                    s.Effect(s => {
                        log.Add($"I1:{s.D(state)}");
                        s.OnCleanup(() => log.Add("I1clean"));
                    });
                });
                s.Effect(s => {
                    log.Add($"O2:{s.D(state)}");
                    s.OnCleanup(() => log.Add("O2clean"));
                    s.Effect(s => {
                        log.Add($"I2:{s.D(state)}");
                        s.OnCleanup(() => log.Add("I2clean"));
                    });
                });
            }));
            CollectionAssert.AreEqual(new[] { "O1:0", "I1:0", "O2:0", "I2:0" }, log);

            log.Clear();
            state.Set(1);
            CollectionAssert.AreEqual(
                new[] { "I1clean", "O1clean", "O1:1", "I1:1", "I2clean", "O2clean", "O2:1", "I2:1" },
                log);
        }

        [Test]
        public void Effect_DeferredExecution_ChildRunsAfterTheAttachingBlock() {
            var log = new List<string>();

            using var tree = SpokeTree.Spawn(new Effect("outer", s => {
                log.Add("outer:start");
                s.Effect(s => log.Add("inner"));
                log.Add("outer:end");
            }));

            CollectionAssert.AreEqual(new[] { "outer:start", "outer:end", "inner" }, log);
        }

        [Test]
        public void EffectT_ComposesInnerReactives_AndExposesDerivedSignal() {
            var input = State.Create(1);
            int output = 0;

            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                var derived = s.Effect(s => {
                    var doubled = s.Memo(s => s.D(input) * 2);
                    var plusOne = s.Memo(s => s.D(doubled) + 1);
                    return plusOne;
                });
                s.Effect(s => output = s.D(derived));
            }));
            Assert.AreEqual(3, output);    // input 1 -> doubled 2 -> plusOne 3

            input.Set(5);
            Assert.AreEqual(11, output);   // input 5 -> doubled 10 -> plusOne 11
        }

        [Test]
        public void Effect_RescheduledByLaterSibling_RerunsBeforeOtherPending() {
            // Effects sort by tree-coord. If E2 (middle) writes a state E1 (earlier) depends on,
            // E1's reschedule is inserted ahead of E3 (later, still on first-tick) in the SpokeTree's
            // pending heap. So E1 re-runs BEFORE E3 ticks for the first time.
            var log = new List<string>();
            var x = State.Create(0);
            var triggered = false;

            // Three sibling effects, composed under a root Effect that wires them up once.
            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                s.Effect(s => log.Add($"E1:{s.D(x)}"));
                s.Effect(s => {
                    log.Add("E2");
                    if (!triggered) {
                        triggered = true;
                        x.Set(1);
                    }
                });
                s.Effect(s => log.Add($"E3:{s.D(x)}"));
            }));

            CollectionAssert.AreEqual(
                new[] { "E1:0", "E2", "E1:1", "E3:1" },
                log,
                "After E2 writes x, E1 must re-run before E3 ticks for the first time");
        }

        [Test]
        public void Effect_SelfReschedules_WhenItWritesAStateItReads() {
            // An effect that writes a state it also reads re-runs itself. Here it increments x each
            // run until x reaches 5, then stops writing — converging to a fixed point.
            var x = State.Create(0);
            var runs = 0;

            using var tree = SpokeTree.Spawn(new Effect("inc", s => {
                runs++;
                var v = s.D(x);
                if (v < 5) x.Set(v + 1);
            }));

            Assert.AreEqual(5, x.Now, "x converges to the fixed point");
            Assert.AreEqual(6, runs, "the effect re-ran itself once per value 0..5");
        }

        [Test]
        public void Effect_CapturedBuilderOutsideMutationWindow_Throws() {
            EffectBuilder captured = default;
            var threw = false;

            using var tree = SpokeTree.Spawn(new Effect("e", s => {
                captured = s;
            }));

            try {
                captured.OnCleanup(() => { });
            } catch (System.InvalidOperationException) {
                threw = true;
            }

            Assert.IsTrue(threw, "Expected InvalidOperationException when using a sealed EffectBuilder");
        }

        [Test]
        public void StaleDependency_FiredMidRerun_BeforeItIsReRead_IsSuppressed() {
            // An effect subscribes to a signal at the point it reads it with s.D(...), and a signal only
            // re-runs the effects that are subscribed to it at the moment it fires. Dependencies are
            // re-established in read order on each run — so if an effect WRITES a signal earlier in its
            // body than it READS it, then at the instant of the write the effect is not yet subscribed to
            // that signal this run, and the write cannot trigger a re-run. The dependency only takes hold
            // once the s.D(...) below the write runs.
            //
            // Below: the effect writes `b` before depending on it, so changing `a` causes exactly ONE
            // re-run. If that write were (incorrectly) treated as a live dependency, `runs` would climb to
            // 3 instead of 2.
            var a = State.Create(0);
            var b = State.Create(-1);
            var runs = 0;

            using var tree = SpokeTree.Spawn(new Effect("stale", s => {
                var av = s.D(a);   // subscribe to a
                b.Set(av);         // write b — not subscribed to b yet this run, so this can't re-trigger us
                _ = s.D(b);        // subscribe to b (happens after the write)
                runs++;
            }));
            Assert.AreEqual(1, runs, "sanity: exactly one run on mount");

            a.Set(5);
            Assert.AreEqual(2, runs,
                "Changing 'a' must cause exactly ONE re-run. Writing 'b' mid-run — before re-reading it — " +
                "must not schedule a redundant second re-run.");
        }

        [Test]
        public void EffectT_BlockReturnsNull_ExposesDefault() {
            var observed = -1;

            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                var derived = s.Effect<int>(s => null);
                s.Effect(s => observed = s.D(derived));
            }));

            Assert.AreEqual(0, observed);
        }

        [Test]
        public void EffectT_BlockReturnsNullAfterValue_ResetsToDefault() {
            var hasValue = State.Create(true);
            var observed = -1;

            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                var derived = s.Effect<int>(s => s.D(hasValue) ? s.Memo(s => 7) : null);
                s.Effect(s => observed = s.D(derived));
            }));
            Assert.AreEqual(7, observed);

            hasValue.Set(false);
            Assert.AreEqual(0, observed);
        }
    }
}
