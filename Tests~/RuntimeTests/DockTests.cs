using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class DockTests : SpokeTestFixture {

        [Test]
        public void Call_AttachesEpoch() {
            var attached = false;
            var ticked = false;
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            dock.Call("k", new LambdaEpoch(s => {
                attached = true;
                return s => {
                    ticked = true;
                };
            }));

            Assert.IsTrue(attached);
            Assert.IsFalse(ticked);

            tree.Flush();
            Assert.IsTrue(ticked);
        }

        [Test]
        public void Call_ChildrenTickInAttachOrder() {
            var ticked = new List<string>();
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            dock.Call("x", new LambdaEpoch(s => s => ticked.Add("x")));
            dock.Call("a", new LambdaEpoch(s => s => ticked.Add("a")));
            dock.Call("m", new LambdaEpoch(s => s => ticked.Add("m")));

            tree.Flush();

            CollectionAssert.AreEqual(new[] { "x", "a", "m" }, ticked,
                "Children tick in attach order");
        }

        [Test]
        public void Call_OnExistingKey_FirstDetachesOldChild() {
            var log = new List<string>();
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            dock.Call("k", new LambdaEpoch(s => {
                s.OnCleanup(() => log.Add("first-cleanup"));
                return null;
            }));

            dock.Call("k", new LambdaEpoch(s => {
                log.Add("second-attached");
                return null;
            }));

            CollectionAssert.AreEqual(
                new[] { "first-cleanup", "second-attached" },
                log);
        }

        [Test]
        public void Drop_MissingKey_IsNoOp() {
            Dock dock = null;
            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            Assert.DoesNotThrow(() => dock.Drop("never-attached"));
        }

        [Test]
        public void Drop_DetachesChild_AndRunsCleanup() {
            var cleaned = false;
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            dock.Call("k", new LambdaEpoch(s => {
                s.OnCleanup(() => cleaned = true);
                return null;
            }));

            dock.Drop("k");
            Assert.IsTrue(cleaned);
        }

        [Test]
        public void Drop_SelfDuringAttach_RunsCleanup_AndDoesNotTick() {
            var log = new List<string>();
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            dock.Call("k", new LambdaEpoch(s => {
                dock.Drop("k");              // drop self while still attaching
                s.OnCleanup(() => log.Add("cleanup"));
                return s => log.Add("tick");
            }));

            tree.Flush();

            Assert.IsNull(tree.Fault);
            CollectionAssert.AreEqual(new[] { "cleanup" }, log,
                "the self-dropped epoch runs its cleanup and never ticks");
        }

        [Test]
        public void Call_SameKeyDuringOwnAttach_ReplacesSelf() {
            var log = new List<string>();
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            dock.Call("k", new LambdaEpoch(s => {
                s.OnCleanup(() => log.Add("old-cleanup"));
                dock.Call("k", new LambdaEpoch(s => {   // re-key self with the same key
                    log.Add("new-attached");
                    return s => log.Add("new-tick");
                }));
                return s => log.Add("old-tick");
            }));

            tree.Flush();

            CollectionAssert.AreEqual(new[] { "new-attached", "old-cleanup", "new-tick" }, log,
                "replacement attaches, then the old epoch cleans up once it finishes; the old never ticks");
            Assert.IsNull(tree.Fault);
        }

        [Test]
        public void Call_UnderHeavyChurn_ChildrenStillTickInAttachOrder() {
            // Cumulative attach count grows far past the packed coordinate range while only
            // a few children stay live; ticks must follow attach order the whole way
            var ticked = new List<int>();
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            for (int round = 0; round < 200; round++) {
                for (int k = 0; k < 4; k++) {
                    int stamp = round * 4 + k;
                    dock.Call(k, new LambdaEpoch(s => s => ticked.Add(stamp)));
                }
                ticked.Clear();
                tree.Flush();
                var expected = new[] { round * 4 + 0, round * 4 + 1, round * 4 + 2, round * 4 + 3 };
                CollectionAssert.AreEqual(expected, ticked, $"attach order broke on round {round}");
            }
        }

        [Test]
        public void Call_UnderHeavyChurn_DescendantsStillTickInTreeCoordOrder() {
            var ticked = new List<string>();
            var ports = new Dictionary<string, EpochPorts>();
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            // Three live children, each with a nested child attached in its Init
            for (int c = 0; c < 3; c++) {
                var name = $"c{c}";
                dock.Call(name, new LambdaEpoch(s => {
                    s.Call(new LambdaEpoch(s => {
                        ports[name + "-inner"] = s.Ports;
                        return s => ticked.Add(name + "-inner");
                    }));
                    ports[name] = s.Ports;
                    return s => ticked.Add(name);
                }));
            }
            tree.Flush();

            // Churn a single key far past the packed coordinate range, renumbering the live children
            for (int i = 0; i < 600; i++) dock.Call("churn", new LambdaEpoch(s => null));
            dock.Drop("churn");
            tree.Flush();

            // Rearm everything in scrambled order; ticks must follow tree-coord order
            ticked.Clear();
            foreach (var key in new[] { "c1", "c2-inner", "c0", "c1-inner", "c2", "c0-inner" }) {
                ports[key].RequestTick();
            }
            tree.Flush();

            CollectionAssert.AreEqual(
                new[] { "c0-inner", "c0", "c1-inner", "c1", "c2-inner", "c2" }, ticked,
                "inner children tick before their parents, parents tick in attach order");
        }

        [Test]
        public void DockCleanup_DetachesChildren_InReverseAttachOrder() {
            var log = new List<string>();
            Dock dock = null;

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            dock.Call("x", new LambdaEpoch(s => { s.OnCleanup(() => log.Add("x")); return null; }));
            dock.Call("a", new LambdaEpoch(s => { s.OnCleanup(() => log.Add("a")); return null; }));
            dock.Call("m", new LambdaEpoch(s => { s.OnCleanup(() => log.Add("m")); return null; }));

            tree.Dispose();

            CollectionAssert.AreEqual(new[] { "m", "a", "x" }, log,
                "Children detach in reverse attach order (not reverse key order, which would be x, m, a)");
        }

        [Test]
        public void CallWhileDetaching_Throws() {
            Exception caught = null;

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                var dock = s.Call(new Dock());
                dock.Call("k", new LambdaEpoch(s => {
                    s.OnCleanup(() => {
                        try {
                            dock.Call("late", new LambdaEpoch(s => null));
                        } catch (Exception ex) {
                            caught = ex;
                        }
                    });
                    return null;
                }));
                return null;
            }));

            tree.Dispose();

            Assert.IsNotNull(caught, "Dock.Call during detaching should throw");
        }

        [Test]
        public void Call_ChildInitThrows_LeavesRuntimeStackBalanced() {
            var log = new List<string>();
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            Assert.Throws<SpokeException>(() => dock.Call("k", new LambdaEpoch(s => {
                s.OnCleanup(() => log.Add("partial-cleanup"));
                throw new Exception("boom");
            })));

            Assert.AreEqual(0, SpokeRuntime.Frames.Count,
                "the Dock frame must be popped even when the child's Init throws");

            // The faulted child stays docked; replacing its key runs the cleanup it
            // registered before throwing, and the dock keeps working
            dock.Call("k", new LambdaEpoch(s => {
                log.Add("replacement-attached");
                return s => log.Add("replacement-tick");
            }));
            tree.Flush();

            CollectionAssert.AreEqual(
                new[] { "partial-cleanup", "replacement-attached", "replacement-tick" }, log);
            Assert.IsNull(tree.Fault);
        }
    }
}
