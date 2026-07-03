using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples.BaseDefence {

    public class BuildMenu : SpokeBehaviour {

        [Serializable]
        struct BuildItem {
            public Building Prefab;
            public string Name;
            public Button Button;
            public string Hotkey;
        }

        [Header("References")]
        [SerializeField] Interface ui;

        [Header("Items")]
        [SerializeField] BuildItem relayItem;
        [SerializeField] BuildItem radarItem;
        [SerializeField] BuildItem turretItem;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                s.Effect(ControlItem(relayItem));
                s.Effect(ControlItem(radarItem));
                s.Effect(ControlItem(turretItem));
            });
        }

        EffectBlock ControlItem(BuildItem item) => s => {
            var buttonText = item.Button.GetComponentInChildren<Text>();
            var idleLabel = $"{item.Name} ({item.Hotkey}) - ${item.Prefab.Cost}";
            buttonText.text = idleLabel;

            var hotkey = item.Hotkey.ToLower();
            var canAfford = s.Memo(s => item.Prefab.Cost <= s.D(GameState.Money));
            var isPlacing = s.Memo(s => s.D(ui.Placing) != null);
            var isNotPlacing = s.Memo(s => !s.D(isPlacing));

            s.Phase(isNotPlacing, s => {
                s.Effect(s => item.Button.interactable = s.D(canAfford));

                void beginPlacing() { if (canAfford.Now) ui.Placing.Set(item.Prefab); }
                s.Subscribe(item.Button.onClick, beginPlacing);
                s.Effect(WatchHotkey(hotkey, beginPlacing));
            });

            s.Phase(isPlacing, s => {
                var isPlacingThis = s.Memo(s => s.D(ui.Placing) == item.Prefab);

                // Only the selected button stays live — it becomes the cancel affordance.
                s.Effect(s => item.Button.interactable = s.D(isPlacingThis));

                s.Phase(isPlacingThis, s => {
                    buttonText.text = $"Cancel ({item.Hotkey})";
                    s.OnCleanup(() => buttonText.text = idleLabel);

                    void cancel() => ui.Placing.Set(null);
                    s.Subscribe(item.Button.onClick, cancel);
                    s.Effect(WatchHotkey("escape", cancel));
                });
            });
        };

        // Invokes onPressed on the frame the key goes down, for as long as this is mounted.
        EffectBlock WatchHotkey(string key, Action onPressed) => s => {
            IEnumerator onUpdate() {
                while (true) {
                    if (Input.GetKeyDown(key)) onPressed();
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };
    }
}