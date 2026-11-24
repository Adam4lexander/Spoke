using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class MemoTests : SpokeTestFixture {

        [Test]
        public void Memo_Now_ReturnsComputedValue() {
            var src = State.Create(5);
            ISignal<int> doubled = null;

            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                doubled = s.Memo(s => s.D(src) * 2);
            }));
            Assert.AreEqual(10, doubled.Now);

            src.Set(7);
            Assert.AreEqual(14, doubled.Now);
        }

        [Test]
        public void Memo_RecomputesOnDependencyChange() {
            var a = State.Create(2);
            var b = State.Create(3);
            ISignal<int> sum = null;

            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                sum = s.Memo(s => s.D(a) + s.D(b));
            }));
            Assert.AreEqual(5, sum.Now);

            a.Set(10);
            Assert.AreEqual(13, sum.Now);

            b.Set(20);
            Assert.AreEqual(30, sum.Now);
        }

        [Test]
        public void Memo_SameComputedValue_DoesNotPropagate() {
            var input = State.Create(1);
            ISignal<int> capped = null;
            var dependentRuns = 0;

            // A Memo plus a dependent Effect, composed under a root Effect that wires them up once.
            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                capped = s.Memo(s => System.Math.Min(s.D(input), 10));
                s.Effect(s => {
                    dependentRuns++;
                    _ = s.D(capped);
                });
            }));
            Assert.AreEqual(1, dependentRuns);

            input.Set(20);
            Assert.AreEqual(10, capped.Now);
            Assert.AreEqual(2, dependentRuns);

            input.Set(30);
            Assert.AreEqual(10, capped.Now);
            Assert.AreEqual(2, dependentRuns);
        }

        [Test]
        public void Memo_OnCleanup_RunsBeforeRerun() {
            var src = State.Create(0);
            var log = new List<string>();

            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                s.Memo(s => {
                    var v = s.D(src);
                    log.Add($"compute:{v}");
                    s.OnCleanup(() => log.Add($"cleanup:{v}"));
                    return v;
                });
            }));

            src.Set(1);

            CollectionAssert.AreEqual(new[] { "compute:0", "cleanup:0", "compute:1" }, log);
        }

        [Test]
        public void Memo_Recompute_PropagatesToDependentEffect() {
            // A Memo feeding a dependent Effect. On mount, both run after the outer that created them
            // (deferred) and in tree-coord order (memo before effect). When src changes, the memo
            // recomputes and the dependent effect re-runs with the new value.
            var log = new List<string>();
            var src = State.Create(0);

            using var tree = SpokeTree.Spawn(new Effect("outer", s => {
                log.Add("outer");
                var memo = s.Memo(s => {
                    log.Add($"memo:{s.D(src)}");
                    return s.D(src) * 2;
                });
                s.Effect(s => log.Add($"inner:{s.D(memo)}"));
            }));
            CollectionAssert.AreEqual(new[] { "outer", "memo:0", "inner:0" }, log);

            log.Clear();
            src.Set(3);
            // the memo settles before the tree ticks, value changes 0 → 6, inner reruns
            CollectionAssert.AreEqual(new[] { "memo:3", "inner:6" }, log);
        }

        // ---------------------------------------------------------------- Outside a tree

        [Test]
        public void Standalone_ComputesWhenCreated_AndFollowsItsDependencies() {
            var a = State.Create(2);
            var b = State.Create(3);
            var runs = 0;

            using var sum = Memo.Create(s => { runs++; return s.D(a) + s.D(b); });
            Assert.AreEqual(1, runs, "Outside of Spoke there's nothing to wait for, so it runs when created");
            Assert.AreEqual(5, sum.Now);

            a.Set(10);
            Assert.AreEqual(13, sum.Now);
            Assert.AreEqual(2, runs);
        }

        [Test]
        public void Standalone_Dispose_StopsRecomputing_AndKeepsLastValue() {
            var src = State.Create(1);
            var runs = 0;
            var cleanups = 0;

            var memo = Memo.Create(s => {
                runs++;
                s.OnCleanup(() => cleanups++);
                return s.D(src) * 2;
            });
            Assert.AreEqual(2, memo.Now);

            memo.Dispose();
            Assert.AreEqual(1, cleanups, "Dispose runs the cleanup declared by the last run");

            src.Set(5);
            Assert.AreEqual(1, runs, "A disposed memo must not recompute");
            Assert.AreEqual(2, memo.Now, "A disposed memo keeps its last value");
        }

        [Test]
        public void Standalone_NotifiesSubscribers_OnlyWhenValueChanges() {
            var input = State.Create(1);
            var seen = new List<int>();

            using var capped = Memo.Create(s => System.Math.Min(s.D(input), 10));
            using var sub = capped.Subscribe(v => seen.Add(v));

            input.Set(5);
            input.Set(20);
            input.Set(30); // still capped at 10

            CollectionAssert.AreEqual(new[] { 5, 10 }, seen);
        }

        [Test]
        public void Standalone_StaticTrigger_Recomputes() {
            var refresh = Trigger.Create();
            var external = 1;

            using var memo = Memo.Create(s => external * 2, refresh);
            Assert.AreEqual(2, memo.Now);

            external = 7;
            Assert.AreEqual(2, memo.Now, "Nothing told the memo that anything changed");

            refresh.Invoke();
            Assert.AreEqual(14, memo.Now);
        }

        // ---------------------------------------------------------------- Inside a tree

        [Test]
        public void Memo_Now_IsComputed_RightAfterItsCreated() {
            var src = State.Create(5);
            var seen = -1;

            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                var doubled = s.Memo(s => s.D(src) * 2);
                seen = doubled.Now;
            }));

            Assert.AreEqual(10, seen, "Reading a memo that hasn't run yet runs it");
        }

        [Test]
        public void Memo_FirstRun_WaitsForTheBlockThatCreatedIt() {
            State<int> late = null;
            ISignal<int> doubled = null;

            // The memo's block reads something that's only assigned further down
            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                doubled = s.Memo(s => s.D(late) * 2);
                late = State.Create(5);
            }));

            Assert.IsNull(tree.Fault);
            Assert.AreEqual(10, doubled.Now);
        }

        [Test]
        public void Memo_IsDisposed_WithTheBlockThatCreatedIt() {
            var src = State.Create(1);
            var runs = 0;
            ISignal<int> doubled = null;

            var tree = SpokeTree.Spawn(new Effect("root", s => {
                doubled = s.Memo(s => { runs++; return s.D(src) * 2; });
            }));
            Assert.AreEqual(1, runs);

            tree.Dispose();
            src.Set(5);

            Assert.AreEqual(1, runs, "The memo must not outlive the effect that created it");
        }

        // ---------------------------------------------------------------- Settling

        [Test]
        public void Memos_Settle_BeforeAnyTreeTicks() {
            var src = State.Create(1);
            var seen = new List<string>();

            using var doubled = Memo.Create(s => s.D(src) * 2);
            using var tree = SpokeTree.Spawn(new Effect("root", s => seen.Add($"{s.D(src)}:{s.D(doubled)}")));
            CollectionAssert.AreEqual(new[] { "1:2" }, seen);

            seen.Clear();
            src.Set(3);

            // One run, and it never sees the new src paired with the old doubled
            CollectionAssert.AreEqual(new[] { "3:6" }, seen);
        }

        [Test]
        public void Memos_Settle_WithoutGlitches_WhateverOrderTheyreMarkedIn() {
            // d reads src directly, and also through b → c. The setup makes d subscribe to src
            // before b does, so d is the first to hear about a change and the first in the queue.
            // It must still wait for c, or it computes from a c that's out of date.
            var src = State.Create(1);
            var bReadsSrc = State.Create(false);
            var runsD = 0;

            using var b = Memo.Create("b", s => s.D(bReadsSrc) ? s.D(src) + 1 : 0);
            using var c = Memo.Create("c", s => s.D(b) * 10);
            using var d = Memo.Create("d", s => { runsD++; return s.D(src) + s.D(c); });
            bReadsSrc.Set(true);
            Assert.AreEqual(1 + 20, d.Now);

            var seen = new List<int>();
            using var sub = d.Subscribe(v => seen.Add(v));
            runsD = 0;

            src.Set(2);

            CollectionAssert.AreEqual(new[] { 2 + 30 }, seen, "Subscribers must only see the settled value");
            Assert.AreEqual(1, runsD, "d should compute once");
        }

        [Test]
        public void Memo_DoesNotRecompute_WhenTheMemoItReadsComputesTheSameValue() {
            var hitpoints = State.Create(10);
            var runs = 0;

            using var isAlive = Memo.Create(s => s.D(hitpoints) > 0);
            using var label = Memo.Create(s => { runs++; return s.D(isAlive) ? "alive" : "dead"; });
            Assert.AreEqual("alive", label.Now);
            runs = 0;

            hitpoints.Set(5);
            Assert.AreEqual("alive", label.Now);
            Assert.AreEqual(0, runs, "isAlive didn't change, so nothing downstream needs to run");

            hitpoints.Set(0);
            Assert.AreEqual("dead", label.Now);
            Assert.AreEqual(1, runs);
        }

        [Test]
        public void Memo_IsUpToDate_WhenReadInsideABatch() {
            var a = State.Create(1);
            var b = State.Create(1);
            var runs = 0;

            using var sum = Memo.Create(s => { runs++; return s.D(a) + s.D(b); });
            using var doubled = Memo.Create(s => s.D(sum) * 2);
            Assert.AreEqual(4, doubled.Now);
            runs = 0;

            var sumInBatch = -1;
            var doubledInBatch = -1;
            SpokeRuntime.Batch(() => {
                a.Set(10);
                b.Set(20);
                doubledInBatch = doubled.Now; // only reads sum, which hasn't run yet
                sumInBatch = sum.Now;
            });

            Assert.AreEqual(30, sumInBatch);
            Assert.AreEqual(60, doubledInBatch);
            Assert.AreEqual(1, runs, "Two changes in one batch should cost one run");
        }

        [Test]
        public void Memo_IsUpToDate_WhenReadInsideASubscribersCallback() {
            var src = State.Create(1);
            var doubledSeen = -1;
            var plusOneSeen = -1;
            Memo<int> doubled = null;
            Memo<int> plusOne = null;

            // Subscribed before either memo exists, so the callback is ahead of them in every way
            // it could be. It must still find them up to date.
            using var sub = src.Subscribe(v => {
                plusOneSeen = plusOne.Now; // only reads doubled, which hasn't run yet
                doubledSeen = doubled.Now;
            });
            doubled = Memo.Create(s => s.D(src) * 2);
            plusOne = Memo.Create(s => s.D(doubled) + 1);

            src.Set(5);

            Assert.AreEqual(10, doubledSeen);
            Assert.AreEqual(11, plusOneSeen);
            doubled.Dispose();
            plusOne.Dispose();
        }

        [Test]
        public void Memo_IsUpToDate_InsideACallback_AfterAReentrantSet() {
            var src = State.Create(0);
            var seen = new List<string>();
            Memo<int> doubled = null;

            // The first subscriber sets src again while it's still being told about the first
            // change. The second is then called for the first change, with src already at 2.
            // Reading the memo before the second set brings it up to date with the first, so it
            // can only be right afterwards if it was told about the second one straight away.
            using var sub1 = src.Subscribe(v => {
                if (v != 1) return;
                Assert.AreEqual(2, doubled.Now);
                src.Set(2);
            });
            using var sub2 = src.Subscribe(v => seen.Add($"told {v}, src is {src.Now}, doubled is {doubled.Now}"));
            doubled = Memo.Create(s => s.D(src) * 2);

            src.Set(1);

            CollectionAssert.AreEqual(new[] { "told 1, src is 2, doubled is 4", "told 2, src is 2, doubled is 4" }, seen);
            doubled.Dispose();
        }

        [Test]
        public void Memo_DropsDependencies_ItNoLongerReads() {
            var useA = State.Create(true);
            var a = State.Create(1);
            var b = State.Create(100);
            var runs = 0;

            using var memo = Memo.Create(s => { runs++; return s.D(useA) ? s.D(a) : s.D(b); });
            Assert.AreEqual(1, memo.Now);

            useA.Set(false);
            Assert.AreEqual(100, memo.Now);
            runs = 0;

            a.Set(2);
            Assert.AreEqual(0, runs, "a is no longer read, so changing it shouldn't run the memo");
        }

        // ---------------------------------------------------------------- Faults and guards

        [Test]
        public void Memo_ThatThrows_RethrowsToReaders_AndRecovers() {
            Errors.ExpectErrors();
            var src = State.Create(1);

            using var memo = Memo.Create("picky", s => {
                var v = s.D(src);
                if (v < 0) throw new System.ArgumentException("negative");
                return v * 2;
            });
            Assert.AreEqual(2, memo.Now);

            src.Set(-1);
            Assert.IsTrue(Errors.Entries.Exists(e => e.msg.Contains("picky") && e.ex is System.ArgumentException),
                "With nobody reading it, the fault should be logged");
            var thrown = Assert.Throws<System.InvalidOperationException>(() => { var _ = memo.Now; });
            Assert.IsInstanceOf<System.ArgumentException>(thrown.InnerException);

            src.Set(4);
            Assert.AreEqual(8, memo.Now, "The memo should recover once its dependency changes");
        }

        [Test]
        public void Effect_ReadingAFaultedMemo_FaultsItsTree() {
            Errors.ExpectErrors();
            var src = State.Create(1);
            var runs = 0;

            using var memo = Memo.Create(s => s.D(src) < 0 ? throw new System.ArgumentException("negative") : s.D(src));
            using var tree = SpokeTree.Spawn(new Effect("root", s => { runs++; s.D(memo); }));
            Assert.IsNull(tree.Fault);

            src.Set(-1);

            Assert.AreEqual(2, runs, "The effect should be told that the memo it reads has faulted");
            Assert.IsNotNull(tree.Fault);
        }

        [Test]
        public void Memo_ReadingItself_Faults() {
            Errors.ExpectErrors();
            var readSelf = State.Create(false);
            Memo<int> memo = null;

            memo = Memo.Create(s => s.D(readSelf) ? s.D(memo) + 1 : 0);
            Assert.AreEqual(0, memo.Now);

            readSelf.Set(true);

            var thrown = Assert.Throws<System.InvalidOperationException>(() => { var _ = memo.Now; });
            StringAssert.Contains("can't depend on itself", thrown.InnerException.Message);
            memo.Dispose();
        }

        [Test]
        public void Effect_IsNotRetriggered_ByAMemoItBroughtUpToDateItself() {
            var src = State.Create(1);
            var rerun = Trigger.Create();
            var runs = 0;

            using var doubled = Memo.Create(s => s.D(src) * 2);
            // Inside the batch the memo is stale when the effect reads it. Reading it runs it, and
            // the effect has then seen the new value. That mustn't count as a change to react to.
            using var tree = SpokeTree.Spawn(new Effect("root", s => {
                runs++;
                SpokeRuntime.Batch(() => {
                    src.Set(src.Now + 1);
                    s.D(doubled);
                });
            }, rerun));
            Assert.AreEqual(1, runs);

            rerun.Invoke();

            Assert.AreEqual(2, runs);
            Assert.IsNull(tree.Fault);
        }

        [Test]
        public void Memo_ThatKeepsInvalidatingItself_IsStopped() {
            Errors.ExpectErrors();
            var counter = State.Create(0);
            var runs = 0;

            // Not a pure function: it writes the state it reads. Stops by itself at 5000 runs, so a
            // broken guard fails the test instead of hanging it
            using var memo = Memo.Create("runaway", s => {
                var v = s.D(counter);
                if (++runs < 5000) counter.Set(v + 1);
                return v;
            });

            Assert.Less(runs, 5000, "The guard should stop the memo before it stops by itself");
            Assert.IsTrue(Errors.Entries.Exists(e => e.msg.Contains("runaway")));
        }

        [Test]
        public void MemoBuilder_ThrowsWhenUsedOutsideItsBlock() {
            var src = State.Create(1);
            MemoBuilder captured = default;

            using var memo = Memo.Create(s => { captured = s; return s.D(src); });

            Assert.Throws<System.InvalidOperationException>(() => captured.D(src));
            Assert.Throws<System.InvalidOperationException>(() => captured.OnCleanup(() => { }));
        }
    }
}
