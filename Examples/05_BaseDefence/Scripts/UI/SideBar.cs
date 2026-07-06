using System;
using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples.BaseDefence {

    [Serializable]
    public class BuildItem {
        public Building Prefab;
        public Button Button;
        public string Hotkey;
        public CoverageType Coverage;   // shown while placing, alongside power coverage
    }

    // The sidebar routes the game mode to one of four panels; each panel class owns
    // its scene refs and the recipe that runs while its mode is active.
    public class SideBar : SpokeBehaviour {

        // Key colours for rich-text accents.
        static readonly Color amber = new(1f, 0.7372549f, 0f);

        [Header("Panels")]
        [SerializeField] Pregame pregame;
        [SerializeField] Gameplay gameplay;
        [SerializeField] EndScreen gameOver;
        [SerializeField] EndScreen victory;

        protected override void Init(EffectBuilder s) {
            pregame.Setup();
            gameplay.Setup();
            gameOver.Setup();
            victory.Setup();

            s.Effect(s => {
                var panel = s.D(GameState.Mode) switch {
                    GameMode.Pregame => pregame.Mount,
                    GameMode.Playing => gameplay.Mount,
                    GameMode.GameOver => gameOver.Mount,
                    _ => victory.Mount,
                };
                s.Effect(panel);
            });
        }

        [Serializable]
        class Pregame {
            [SerializeField] GameObject root;
            [SerializeField] Button startButton;

            public void Setup() => root.SetActive(false);

            public EffectBlock Mount => s => {
                root.SetActive(true);
                s.OnCleanup(() => root.SetActive(false));
                s.Subscribe(startButton.onClick, () => GameState.Mode.Set(GameMode.Playing));
            };
        }

        [Serializable]
        class Gameplay {
            [SerializeField] BoardInteractions interactions;
            [SerializeField] GameObject root;
            [SerializeField] Text waveText;
            [SerializeField] Text moneyText;
            [SerializeField] Text resourcesText;
            [SerializeField] Text messageText;
            [SerializeField] BuildItem[] buildItems;

            public void Setup() {
                messageText.text = "";
                root.SetActive(false);
            }

            public EffectBlock Mount => s => {
                root.SetActive(true);
                s.OnCleanup(() => root.SetActive(false));

                s.Effect(s => {
                    var money = $"${s.D(GameState.Money)} (+{s.D(GameState.CollectRate):0.#})";
                    var size = Mathf.RoundToInt(moneyText.fontSize * 0.6f);
                    var colour = ColorUtility.ToHtmlStringRGBA(amber);
                    moneyText.text = s.D(GameState.Director.IsAssaulting)
                        ? $"{money}\n<size={size}><color=#{colour}>harvesting paused</color></size>"
                        : money;
                });

                s.Effect(s => resourcesText.text = $"Resources left: {s.D(GameState.ResourcesRemaining)}");

                // Whole seconds derived from the ticking countdown, so the text only
                // rewrites when the displayed number changes.
                var countdown = s.Memo(s => Mathf.CeilToInt(s.D(GameState.Director.NextWaveIn)));

                s.Effect(s => {
                    var front = s.D(GameState.Director.Front);
                    var direction = front.ToString().ToLower();
                    if (s.D(GameState.Director.IsAssaulting)) waveText.text = $"Wave {s.D(GameState.Director.Wave)} — attacking from the {direction}";
                    else if (front != WaveFront.None) waveText.text = $"Wave {s.D(GameState.Director.Wave) + 1} from the {direction} in {s.D(countdown)}s";
                    else waveText.text = $"Wave {s.D(GameState.Director.Wave) + 1} in {s.D(countdown)}s";
                });

                s.Effect(ShowMessage);

                foreach (var item in buildItems) s.Effect(ControlBuildItem(item));
            };

            // The message line: placement instructions take priority, then the hovered unit's description.
            EffectBlock ShowMessage => s => {
                var placing = s.D(interactions.Placing);
                var hovered = s.D(interactions.Hovering);
                if (placing != null) messageText.text = $"Placing {placing.Prefab.DisplayName} — press Escape to cancel";
                else if (hovered != null) messageText.text = s.D(hovered.HoverInfo).Description;
                else messageText.text = "";
            };

            EffectBlock ControlBuildItem(BuildItem item) => s => {
                var buttonText = item.Button.GetComponentInChildren<Text>();
                var idleLabel = $"{item.Prefab.DisplayName} ({item.Hotkey}) - ${item.Prefab.Cost}";
                buttonText.text = idleLabel;

                var hotkey = item.Hotkey.ToLower();
                var canAfford = s.Memo(s => item.Prefab.Cost <= s.D(GameState.Money));
                var isPlacing = s.Memo(s => s.D(interactions.Placing) != null);
                var isNotPlacing = s.Memo(s => !s.D(isPlacing));

                s.Phase(isNotPlacing, s => {
                    s.Effect(s => item.Button.interactable = s.D(canAfford));

                    void beginPlacing() { if (canAfford.Now) interactions.Placing.Set(item); }
                    s.Subscribe(item.Button.onClick, beginPlacing);
                    s.Subscribe(InputSignals.KeyDown(hotkey), beginPlacing);
                });

                s.Phase(isPlacing, s => {
                    var isPlacingThis = s.Memo(s => s.D(interactions.Placing) == item);

                    // Only the selected button stays live — it becomes the cancel affordance.
                    s.Effect(s => item.Button.interactable = s.D(isPlacingThis));

                    s.Phase(isPlacingThis, s => {
                        buttonText.text = $"Cancel ({item.Hotkey})";
                        s.OnCleanup(() => buttonText.text = idleLabel);

                        void cancel() => interactions.Placing.Set(null);
                        s.Subscribe(item.Button.onClick, cancel);
                        s.Subscribe(InputSignals.KeyDown("escape"), cancel);
                    });
                });
            };
        }

        [Serializable]
        class EndScreen {
            [SerializeField] GameObject root;
            [SerializeField] Button restartButton;

            public void Setup() => root.SetActive(false);

            public EffectBlock Mount => s => {
                root.SetActive(true);
                s.OnCleanup(() => root.SetActive(false));
                s.Subscribe(restartButton.onClick, GameState.Restart);
            };
        }
    }
}
