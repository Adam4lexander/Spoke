using System;
using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples.BaseDefence {

    // A buildable item in the sidebar: the prefab to place, plus its button and hotkey.
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
        static readonly Color danger = new(1f, 0.3f, 0.3f);
        static readonly Color paleBlue = new(0.6f, 0.8f, 1f);

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
                    moneyText.text = s.D(GameState.Director.Wave).IsAssaulting
                        ? $"{money}\n<size={size}><color=#{colour}>harvesting paused</color></size>"
                        : money;
                });

                s.Effect(s => resourcesText.text = $"Resource Sites: {s.D(GameState.ResourcesRemaining)}");

                s.Effect(s => {
                    var wave = s.D(GameState.Director.Wave);
                    var direction = wave.Front.ToString();
                    var header = $"<b>Wave {wave.Number}</b>";
                    if (wave.IsAssaulting) {
                        var colour = ColorUtility.ToHtmlStringRGBA(danger);
                        waveText.text = $"{header}\n<color=#{colour}><b>{direction} attacking</b></color>";
                    } else if (wave.Front != WaveFront.None) {
                        var colour = ColorUtility.ToHtmlStringRGBA(CountdownColour(wave.StartsIn));
                        waveText.text = $"{header}\n{direction} in <color=#{colour}>{wave.StartsIn}s</color>";
                    } else {
                        var colour = ColorUtility.ToHtmlStringRGBA(CountdownColour(wave.StartsIn));
                        waveText.text = $"{header}\nin <color=#{colour}>{wave.StartsIn}s</color>";
                    }
                });

                s.Effect(ShowMessage);

                foreach (var item in buildItems) s.Effect(ControlBuildItem(item));
            };

            // Pale blue far out, sliding through amber to red as the wave gets close.
            static Color CountdownColour(int startsIn) {
                var lull = GameState.Director.LullDuration;
                var closeness = lull > 0f ? Mathf.Clamp01(1f - startsIn / lull) : 1f;
                return closeness < 0.5f
                    ? Color.Lerp(paleBlue, amber, closeness / 0.5f)
                    : Color.Lerp(amber, danger, (closeness - 0.5f) / 0.5f);
            }

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

                    // Only the selected button stays live, becoming the cancel affordance.
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
