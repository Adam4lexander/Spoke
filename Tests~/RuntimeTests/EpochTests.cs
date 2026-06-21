using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    public class EpochTests : SpokeTestFixture {

        sealed class CounterDisposable : IDisposable {
            public int Disposed;
            public void Dispose() => Disposed++;
        }

        sealed class ThrowingDisposable : IDisposable {
            public void Dispose() => throw new Exception("dispose boom");
        }

        [Test]
        public void Init_RunsSynchronously_DuringCall() {
            var log = new List<string>();

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                log.Add("parent-init-before-call");
                s.Call(new LambdaEpoch(s => {
                    log.Add("child-init");
                    return null;
                }));
                log.Add("parent-init-after-call");
                return null;
            }));

            CollectionAssert.AreEqual(
                new[] { "parent-init-before-call", "child-init", "parent-init-after-call" },
                log);
        }

        [Test]
        public void TickBlock_NotInvoked_DuringConstruction_OnlyOnFlush() {
            var initRan = false;
            var tickRan = false;

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                initRan = true;
                return s => tickRan = true;
            }));
            try {
                Assert.IsTrue(initRan);
                Assert.IsFalse(tickRan, "TickBlock must not run during construction");

                tree.Flush();
                Assert.IsTrue(tickRan);
            } finally {
                tree.Dispose();
            }
        }

        [Test]
        public void Use_IDisposable_DisposedOnDetach() {
            var resource = new CounterDisposable();

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Use(resource);
                return null;
            }));
            Assert.AreEqual(0, resource.Disposed);

            tree.Dispose();
            Assert.AreEqual(1, resource.Disposed);
        }

        [Test]
        public void Use_SpokeHandle_DisposedOnDetach() {
            var disposed = 0;
            var handle = SpokeHandle.Of(0, _ => disposed++);

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Use(handle);
                return null;
            }));
            Assert.AreEqual(0, disposed);

            tree.Dispose();
            Assert.AreEqual(1, disposed);
        }

        [Test]
        public void Export_VisibleTo_LaterSiblings_NotEarlier() {
            bool? earlySaw = null;
            bool? lateSaw = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Call(new LambdaEpoch(s => {
                    earlySaw = s.TryImport<string>(out _);
                    return null;
                }));
                s.Export("hello");
                s.Call(new LambdaEpoch(s => {
                    lateSaw = s.TryImport<string>(out var v) && v == "hello";
                    return null;
                }));
                return null;
            }));

            Assert.IsFalse(earlySaw.Value, "Earlier sibling should not see later Export");
            Assert.IsTrue(lateSaw.Value, "Later sibling should see earlier Export");
        }

        [Test]
        public void Import_WalksUp_ToAncestor() {
            string deepImport = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Export("from-root");
                s.Call(new LambdaEpoch(s => {
                    s.Call(new LambdaEpoch(s => {
                        s.TryImport<string>(out deepImport);
                        return null;
                    }));
                    return null;
                }));
                return null;
            }));

            Assert.AreEqual("from-root", deepImport);
        }

        [Test]
        public void Import_NearestExport_ShadowsFarther() {
            string imported = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Export("from-root");
                s.Call(new LambdaEpoch(s => {
                    s.Export("from-child"); // shadows the root's string export for this subtree
                    s.Call(new LambdaEpoch(s => {
                        s.TryImport<string>(out imported);
                        return null;
                    }));
                    return null;
                }));
                return null;
            }));

            Assert.AreEqual("from-child", imported,
                "Import should resolve the nearest export, shadowing a farther one of the same type");
        }

        [Test]
        public void Import_NotFound_Throws() {
            Exception captured = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Call(new LambdaEpoch(s => {
                    try {
                        s.Import<DateTime>();
                    } catch (Exception ex) {
                        captured = ex;
                    }
                    return null;
                }));
                return null;
            }));

            Assert.IsNotNull(captured);
        }

        [Test]
        public void TryImport_NotFound_ReturnsFalse() {
            bool? found = null;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                found = s.TryImport<DateTime>(out _);
                return null;
            }));

            Assert.IsFalse(found.Value);
        }

        [Test]
        public void Call_AlreadyAttachedEpoch_FaultsTheTree() {
            var sharedChild = new LambdaEpoch(s => null);
            using var tree1 = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Call(sharedChild);
                return null;
            }));
            Assert.IsNull(tree1.Fault);

            Errors.ExpectErrors();
            using var tree2 = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Call(sharedChild); // InvalidOperationException — already attached
                return null;
            }));

            Assert.IsNotNull(tree2.Fault, "Second tree should be faulted from re-attach attempt");
            Assert.IsInstanceOf<InvalidOperationException>(tree2.Fault.InnerException);
        }

        [Test]
        public void CapturedEpochBuilder_OutsideMutationWindow_Throws() {
            EpochBuilder captured = default;

            using var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                captured = s;
                return null;
            }));

            Assert.Throws<InvalidOperationException>(() => captured.OnCleanup(() => { }));
        }

        [Test]
        public void Detach_RunsAttachmentsInReverseOrder_AcrossMixedTypes() {
            var log = new List<string>();
            var resourceA = new CounterDisposable();
            var resourceB = new CounterDisposable();

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Call(new LambdaEpoch(s => {
                    s.OnCleanup(() => log.Add("childA-cleanup"));
                    return null;
                }));
                s.Use(resourceA);
                s.OnCleanup(() => log.Add("cleanup-mid"));
                s.Use(resourceB);
                s.Call(new LambdaEpoch(s => {
                    s.OnCleanup(() => log.Add("childB-cleanup"));
                    return null;
                }));
                return null;
            }));

            tree.Dispose();

            CollectionAssert.AreEqual(
                new[] { "childB-cleanup", "cleanup-mid", "childA-cleanup" },
                log);
            Assert.AreEqual(1, resourceA.Disposed);
            Assert.AreEqual(1, resourceB.Disposed);
        }

        [Test]
        public void CleanupException_IsSwallowed_AndSubsequentCleanupsStillRun() {
            var lastRan = false;
            Errors.ExpectErrors();

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.OnCleanup(() => throw new Exception("boom"));
                s.OnCleanup(() => lastRan = true);
                return null;
            }));
            tree.Dispose();

            Assert.IsTrue(lastRan, "Later OnCleanup should run even when a sibling cleanup throws");
            Assert.Greater(Errors.Entries.Count, 0, "The runtime should have logged at least one entry");
        }

        [Test]
        public void DisposeException_IsSwallowed_AndSubsequentCleanupsStillRun() {
            var lastRan = false;
            Errors.ExpectErrors();

            var tree = SpokeTree.SpawnManual(new LambdaEpoch(s => {
                s.Use(new ThrowingDisposable());
                s.OnCleanup(() => lastRan = true);
                return null;
            }));
            tree.Dispose();

            Assert.IsTrue(lastRan);
            Assert.Greater(Errors.Entries.Count, 0);
        }

        [Test]
        public void Services_PassedToSpokeTree_AreImportableByDescendants() {
            string imported = null;

            using var tree = SpokeTree.SpawnManual(
                new LambdaEpoch(s => {
                    s.TryImport<string>(out imported);
                    return null;
                }),
                "service-payload"
            );

            Assert.AreEqual("service-payload", imported);
        }
    }
}
