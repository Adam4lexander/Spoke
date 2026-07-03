using System;
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

        [Header("Items")]
        [SerializeField] BuildItem relayItem;
        [SerializeField] BuildItem radarItem;
        [SerializeField] BuildItem turretItem;

        State<Building> placing = new();
        public ISignal<Building> Placing => placing;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                s.Effect(ControlItem(relayItem));
                s.Effect(ControlItem(radarItem));
                s.Effect(ControlItem(turretItem));
            });
        }

        EffectBlock ControlItem(BuildItem item) => s => {
            var buttonText = item.Button.GetComponentInChildren<Text>();
            buttonText.text = $"{item.Name} ({item.Hotkey}) - ${item.Prefab.Cost}";

            var canAfford = s.Memo(s => item.Prefab.Cost <= s.D(GameState.Money));
            var isPlacing = s.Memo(s => s.D(placing) != null);
            var isNotPlacing = s.Memo(s => !s.D(isPlacing));

            s.Phase(isNotPlacing, s => {
                // Subscribe to button if we can afford, and also the hotkeys
                // Disable button if we cant afford
            });

            s.Phase(isPlacing, s => {
                // Some changes to the button given we're placing a building
            });
        };
    }
}