using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Spoke.Tests {

    [TestFixture]
    [NonParallelizable]
    public class TriggerTests : SpokeTestFixture {

        [Test]
        public void Trigger_FiresAllSubscribers() {
            var trigger = Trigger.Create();
            var a = 0;
            var b = 0;

            using var ha = trigger.Subscribe(() => a++);
            using var hb = trigger.Subscribe(() => b++);

            trigger.Invoke();
            trigger.Invoke();

            Assert.AreEqual(2, a);
            Assert.AreEqual(2, b);
        }

        [Test]
        public void Trigger_Unsubscribe_ByDisposeHandle_StopsFurtherInvocations() {
            var trigger = Trigger.Create();
            var count = 0;

            var sub = trigger.Subscribe(() => count++);
            trigger.Invoke();
            sub.Dispose();
            trigger.Invoke();

            Assert.AreEqual(1, count);
        }

        [Test]
        public void Trigger_Unsubscribe_ByAction_RemovesByDelegate() {
            var trigger = Trigger.Create();
            var count = 0;
            Action handler = () => count++;

            var _ = trigger.Subscribe(handler);
            trigger.Invoke();
            trigger.Unsubscribe(handler);
            trigger.Invoke();

            Assert.AreEqual(1, count);
        }

        [Test]
        public void Trigger_FiresInSubscriptionOrder_WithGapsFromUnsubscribes() {
            var t = Trigger.Create();
            var order = new List<int>();
            var handles = new List<SpokeHandle>();
            for (var i = 0; i < 200; i++) {
                var n = i;
                handles.Add(t.Subscribe(() => order.Add(n)));
            }
            for (var i = 0; i < 200; i += 2) handles[i].Dispose();

            t.Invoke();

            var expected = new List<int>();
            for (var i = 1; i < 200; i += 2) expected.Add(i);
            CollectionAssert.AreEqual(expected, order);
            foreach (var h in handles) h.Dispose();
        }

        // Thousands of subscribe/unsubscribe pairs without an invoke between them
        [Test]
        public void Trigger_ChurnedWithoutInvoking_StillDeliversToLiveSubscribers() {
            var t = Trigger.Create();
            var hits = 0;
            using var resident = t.Subscribe(() => hits++);
            for (var i = 0; i < 5000; i++) {
                var h = t.Subscribe(() => hits += 100);
                h.Dispose();
            }

            t.Invoke();

            Assert.AreEqual(1, hits);
        }

        [Test]
        public void TriggerT_SubscribeWithPayload_ReceivesPayload() {
            var t = Trigger.Create<int>();
            var seen = -1;
            using var sub = t.Subscribe((int v) => seen = v);

            t.Invoke(42);
            Assert.AreEqual(42, seen);
        }

        [Test]
        public void TriggerT_SubscribeWithoutPayload_StillNotifiedOnPayloadInvoke() {
            var t = Trigger.Create<int>();
            var ran = false;
            using var sub = t.Subscribe(() => ran = true);

            t.Invoke(7);
            Assert.IsTrue(ran);
        }

        [Test]
        public void Trigger_ReentrantInvoke_IsQueued() {
            var t = Trigger.Create<int>();
            var seen = new List<int>();
            var calls = 0;
            using var sub = t.Subscribe((int v) => {
                seen.Add(v);
                calls++;
                if (calls == 1) {
                    t.Invoke(99);  // re-entrant
                }
            });

            t.Invoke(1);
            CollectionAssert.AreEqual(new[] { 1, 99 }, seen);
        }

        [Test]
        public void Trigger_SubscriberException_IsSwallowed_OthersStillFire() {
            Errors.ExpectErrors();
            var t = Trigger.Create();
            var second = false;
            using var s1 = t.Subscribe(() => throw new Exception("boom"));
            using var s2 = t.Subscribe(() => second = true);

            t.Invoke();

            Assert.IsTrue(second, "Second subscriber should still fire after first throws");
            Assert.Greater(Errors.Entries.Count, 0);
        }

        [Test]
        public void Trigger_SubscribeDuringInvoke_NotNotifiedThisRound() {
            var t = Trigger.Create();
            var calls = 0;
            SpokeHandle lateSub = default;
            using var sub = t.Subscribe(() => {
                calls++;
                if (calls == 1) {
                    lateSub = t.Subscribe(() => calls++);
                }
            });

            t.Invoke();  // Only the existing sub fires; lateSub added during invoke
            Assert.AreEqual(1, calls);

            t.Invoke();  // Now both fire
            Assert.AreEqual(3, calls);
            lateSub.Dispose();
        }

        // The subscriber set is fixed when the dispatch begins. One that an earlier subscriber
        // unsubscribes mid-dispatch was still in that set, so it fires.
        [Test]
        public void Trigger_UnsubscribeDuringInvoke_StillFiresThisRound() {
            var t = Trigger.Create();
            var lateFired = 0;
            SpokeHandle second = default;
            using var first = t.Subscribe(() => second.Dispose());
            second = t.Subscribe(() => lateFired++);

            t.Invoke();
            Assert.AreEqual(1, lateFired);

            t.Invoke();
            Assert.AreEqual(1, lateFired, "must not fire again on later events");
        }

        // Unsubscribing during one event still removes the subscriber from the events after it,
        // including ones already queued by a re-entrant invoke.
        [Test]
        public void Trigger_UnsubscribedDuringEarlierEvent_DoesNotFireOnQueuedEvent() {
            var t = Trigger.Create<int>();
            var seen = new List<int>();
            SpokeHandle victim = default;
            var calls = 0;
            using var driver = t.Subscribe((int v) => {
                calls++;
                if (calls == 1) {
                    t.Invoke(99);      // queued, dispatched once this one completes
                    victim.Dispose();
                }
            });
            victim = t.Subscribe((int v) => seen.Add(v));

            t.Invoke(1);

            CollectionAssert.AreEqual(new[] { 1 }, seen);
        }

        // A subscriber is allowed to unsubscribe others and to subscribe, both while the trigger
        // is dispatching. The set fixed at dispatch start still gets exactly one call each: the
        // mass-unsubscribed still fire this round, the mid-dispatch addition doesn't.
        [Test]
        public void Trigger_MassUnsubscribeThenSubscribe_DuringDispatch_DeliversToDispatchStartSet() {
            var t = Trigger.Create();
            var handles = new List<SpokeHandle>();
            var survivorsFired = 0;
            var addedFired = 0;

            // The first subscriber tears down most of the list, then subscribes, all mid-dispatch
            var first = t.Subscribe(() => {
                for (var i = 100; i < handles.Count; i++) handles[i].Dispose();
                handles.Add(t.Subscribe(() => addedFired++));
            });

            for (var i = 0; i < 1000; i++) {
                handles.Add(t.Subscribe(() => survivorsFired++));
            }

            t.Invoke();

            Assert.AreEqual(1000, survivorsFired, "everyone in the dispatch-start set fires, unsubscribed or not");
            Assert.AreEqual(0, addedFired, "subscribed mid-dispatch, so not in the set");

            first.Dispose();
            foreach (var h in handles) h.Dispose();
        }

        [Test]
        public void TriggerInvoke_BatchesStateMutations_NoIntermediateObservation() {
            // A Trigger.Invoke batches the state mutations its subscribers make: a dependent effect
            // observes only the final combined value (0 then 30), never an intermediate where v1 has
            // updated but v2 has not.
            var t = Trigger.Create();
            var v1 = State.Create(0);
            var v2 = State.Create(0);
            var observations = new List<int>();

            using var tree = SpokeTree.Spawn(new Effect("init", s => {
                s.Effect(s => observations.Add(s.D(v1) + s.D(v2)));
                s.Subscribe(t, () => {
                    v1.Set(10);
                    v2.Set(20);
                });
            }));
            CollectionAssert.AreEqual(new[] { 0 }, observations);

            t.Invoke();

            CollectionAssert.AreEqual(new[] { 0, 30 }, observations);
        }

        // ---------------------------------------------------------------- Dependants

        [Test]
        public void Dependants_AreTold_BeforeAnySubscriber() {
            var trigger = Trigger.Create();
            var log = new List<string>();

            // The subscribers come first, and the dependant is added between them
            using var sub1 = trigger.Subscribe(() => log.Add("subscriber 1"));
            using var dep = (trigger as Trigger.Friend).AddDependant(() => log.Add("dependant"));
            using var sub2 = trigger.Subscribe(() => log.Add("subscriber 2"));

            trigger.Invoke();

            CollectionAssert.AreEqual(new[] { "dependant", "subscriber 1", "subscriber 2" }, log);
        }

        [Test]
        public void Dependants_AreToldRightAway_WhenTheInvokeIsReentrant() {
            var trigger = Trigger.Create<int>();
            var log = new List<string>();

            using var dep = (trigger as Trigger.Friend).AddDependant(() => log.Add("dependant"));
            using var sub1 = trigger.Subscribe(v => {
                log.Add($"first:{v}");
                if (v == 1) trigger.Invoke(2); // subscribers hear about this one after the current event
            });
            using var sub2 = trigger.Subscribe(v => log.Add($"second:{v}"));

            trigger.Invoke(1);

            // The dependant hears about the second invoke before 'second' has even been called for the first
            CollectionAssert.AreEqual(new[] { "dependant", "first:1", "dependant", "second:1", "first:2", "second:2" }, log);
        }

        [Test]
        public void Dependant_DisposingItsHandle_StopsItBeingTold() {
            var trigger = Trigger.Create();
            var count = 0;

            var dep = (trigger as Trigger.Friend).AddDependant(() => count++);
            trigger.Invoke();
            dep.Dispose();
            dep.Dispose(); // harmless a second time
            trigger.Invoke();

            Assert.AreEqual(1, count);
        }

        [Test]
        public void Dependants_RemovedOrAddedDuringAPass_DontDisturbIt() {
            var trigger = Trigger.Create();
            var friend = trigger as Trigger.Friend;
            var log = new List<string>();
            SpokeHandle b = default;

            using var a = friend.AddDependant(() => {
                log.Add("a");
                b.Dispose(); // removes one the pass hasn't reached yet
                friend.AddDependant(() => log.Add("late"));
            });
            b = friend.AddDependant(() => log.Add("b"));
            using var c = friend.AddDependant(() => log.Add("c"));

            trigger.Invoke();

            CollectionAssert.AreEqual(new[] { "a", "c" }, log, "The removed one isn't told, and the new one waits for the next invoke");
        }

        [Test]
        public void Dependants_SurviveHeavyChurn() {
            // Adding and removing leaves tombstones behind, which get swept. The survivors must
            // still be told, in the order they were added, and still be removable by their handles
            var trigger = Trigger.Create();
            var friend = trigger as Trigger.Friend;
            var log = new List<int>();
            var kept = new List<SpokeHandle>();

            for (int i = 0; i < 200; i++) {
                int id = i;
                var handle = friend.AddDependant(() => log.Add(id));
                if (i % 10 == 0) kept.Add(handle);
                else handle.Dispose();
            }

            trigger.Invoke();
            Assert.AreEqual(20, log.Count);
            CollectionAssert.IsOrdered(log);

            log.Clear();
            foreach (var h in kept) h.Dispose();
            trigger.Invoke();
            Assert.IsEmpty(log);
        }

        [Test]
        public void Effect_Dependencies_AreDependants_NotSubscribers() {
            // An effect that's rescheduled from inside a subscriber's callback proves it didn't
            // have to wait its turn behind that subscriber to be told.
            var state = State.Create(0);
            var runs = 0;
            var pendingSeenBySubscriber = false;
            SpokeTree<Effect> tree = null;

            using var sub = state.Subscribe(v => pendingSeenBySubscriber = tree != null && tree.HasPending);
            tree = SpokeTree.Spawn(new Effect("root", s => { runs++; s.D(state); }));

            state.Set(1);

            Assert.IsTrue(pendingSeenBySubscriber, "The effect should already be scheduled when the subscriber is called");
            Assert.AreEqual(2, runs);
            tree.Dispose();
        }
    }
}
