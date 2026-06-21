using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class DockTests : SpokeTestFixture {

        [Test]
        public void Call_AttachesEpoch_AndRunsInit() {
            var attached = false;
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            dock.Call("k", new LambdaEpoch(s => {
                attached = true;
                return null;
            }));

            Assert.IsTrue(attached);
        }

        [Test]
        public void Call_OnExistingKey_FirstDetachesOldChild() {
            var firstCleanup = false;
            var secondAttached = false;
            Dock dock = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            dock.Call("k", new LambdaEpoch(s => {
                s.OnCleanup(() => firstCleanup = true);
                return null;
            }));
            Assert.IsFalse(firstCleanup);

            dock.Call("k", new LambdaEpoch(s => {
                secondAttached = true;
                return null;
            }));

            Assert.IsTrue(firstCleanup, "Old child at key should be detached first");
            Assert.IsTrue(secondAttached);
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
        public void DockCleanup_DetachesChildren_InReverseAttachOrder() {
            var log = new List<string>();
            Dock dock = null;

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                dock = s.Call(new Dock());
                return null;
            }));

            dock.Call("a", new LambdaEpoch(s => { s.OnCleanup(() => log.Add("a")); return null; }));
            dock.Call("b", new LambdaEpoch(s => { s.OnCleanup(() => log.Add("b")); return null; }));
            dock.Call("c", new LambdaEpoch(s => { s.OnCleanup(() => log.Add("c")); return null; }));

            tree.Dispose();

            CollectionAssert.AreEqual(new[] { "c", "b", "a" }, log);
        }

        [Test]
        public void CallWhileDetaching_Throws() {
            Exception caught = null;

            using (var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
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
            }))) {
                // Tree disposes here on scope exit
            }

            Assert.IsNotNull(caught, "Dock.Call during detaching should throw");
        }
    }
}
