using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class MemoTests : SpokeTestFixture {

        [Test]
        public void Memo_Now_ReturnsComputedValue() {
            var src = State.Create(5);
            ISignal<int> doubled = null;

            using var tree = SpokeTree.SpawnManual(new Effect("root", s => {
                doubled = s.Memo(s => s.D(src) * 2);
            }));
            tree.Flush();
            Assert.AreEqual(10, doubled.Now);

            src.Set(7);
            tree.Flush();
            Assert.AreEqual(14, doubled.Now);
        }

        [Test]
        public void Memo_RecomputesOnDependencyChange() {
            var a = State.Create(2);
            var b = State.Create(3);
            ISignal<int> sum = null;

            using var tree = SpokeTree.SpawnManual(new Effect("root", s => {
                sum = s.Memo(s => s.D(a) + s.D(b));
            }));
            tree.Flush();
            Assert.AreEqual(5, sum.Now);

            a.Set(10);
            tree.Flush();
            Assert.AreEqual(13, sum.Now);

            b.Set(20);
            tree.Flush();
            Assert.AreEqual(30, sum.Now);
        }

        [Test]
        public void Memo_SameComputedValue_DoesNotPropagate() {
            var input = State.Create(1);
            ISignal<int> capped = null;
            var dependentRuns = 0;

            // A Memo plus a dependent Effect, composed under a root Effect that wires them up once.
            using var tree = SpokeTree.SpawnManual(new Effect("root", s => {
                capped = s.Memo(s => System.Math.Min(s.D(input), 10));
                s.Effect(s => {
                    dependentRuns++;
                    _ = s.D(capped);
                });
            }));
            tree.Flush();
            Assert.AreEqual(1, dependentRuns);

            input.Set(20);
            tree.Flush();
            Assert.AreEqual(10, capped.Now);
            Assert.AreEqual(2, dependentRuns);

            input.Set(30);
            tree.Flush();
            Assert.AreEqual(10, capped.Now);
            Assert.AreEqual(2, dependentRuns);
        }

        [Test]
        public void Memo_OnCleanup_RunsBeforeRerun() {
            var src = State.Create(0);
            var log = new List<string>();

            using var tree = SpokeTree.SpawnManual(new Effect("root", s => {
                s.Memo(s => {
                    var v = s.D(src);
                    log.Add($"compute:{v}");
                    s.OnCleanup(() => log.Add($"cleanup:{v}"));
                    return v;
                });
            }));
            tree.Flush();

            src.Set(1);
            tree.Flush();

            CollectionAssert.AreEqual(new[] { "compute:0", "cleanup:0", "compute:1" }, log);
        }

        [Test]
        public void Memo_Recompute_PropagatesToDependentEffect() {
            // A Memo feeding a dependent Effect. On mount, both run after the outer that created them
            // (deferred) and in tree-coord order (memo before effect). When src changes, the memo
            // recomputes and the dependent effect re-runs with the new value.
            var log = new List<string>();
            var src = State.Create(0);

            using var tree = SpokeTree.SpawnManual(new Effect("outer", s => {
                log.Add("outer");
                var memo = s.Memo(s => {
                    log.Add($"memo:{s.D(src)}");
                    return s.D(src) * 2;
                });
                s.Effect(s => log.Add($"inner:{s.D(memo)}"));
            }));
            tree.Flush();
            CollectionAssert.AreEqual(new[] { "outer", "memo:0", "inner:0" }, log);

            log.Clear();
            src.Set(3);
            tree.Flush();
            // memo ticks first (coord-sorted earlier), value changes 0 → 6, inner reruns
            CollectionAssert.AreEqual(new[] { "memo:3", "inner:6" }, log);
        }
    }
}
