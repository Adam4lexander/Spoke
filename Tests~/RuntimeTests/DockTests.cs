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
    }
}
