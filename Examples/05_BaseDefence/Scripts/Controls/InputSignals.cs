using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Publishes input events as triggers, so components subscribe to the events
    // they care about while mounted instead of polling Input themselves.
    public class InputSignals : SpokeSingleton<InputSignals> {

        Trigger leftClick = Trigger.Create();
        Trigger rightClick = Trigger.Create();
        Dictionary<string, Trigger> keyDowns = new();
        List<Trigger> firing = new();

        public static ITrigger LeftClick => Instance.leftClick;
        public static ITrigger RightClick => Instance.rightClick;

        // A trigger for the frames the given key goes down, created on first request.
        public static ITrigger KeyDown(string key) {
            var keys = Instance.keyDowns;
            if (!keys.TryGetValue(key, out var trigger)) keys[key] = trigger = Trigger.Create();
            return trigger;
        }

        protected override void Init(EffectBuilder s) { }

        void Update() {
            if (Input.GetMouseButtonDown(0)) leftClick.Invoke();
            if (Input.GetMouseButtonDown(1)) rightClick.Invoke();

            // A handler may request a new key mid-invoke, so collect the triggers
            // to fire before invoking any of them.
            foreach (var kv in keyDowns) {
                if (Input.GetKeyDown(kv.Key)) firing.Add(kv.Value);
            }
            foreach (var trigger in firing) trigger.Invoke();
            firing.Clear();
        }
    }
}
