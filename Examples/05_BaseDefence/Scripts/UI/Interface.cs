using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Spoke.Examples.BaseDefence {

    public class Interface : SpokeBehaviour {

        [Header("Pregame References")]
        [SerializeField] GameObject pregamePanel;
        [SerializeField] Button startButton;

        [Header("Gameplay References")]
        [SerializeField] GameObject gameplayPanel;
        [SerializeField] WaveDirector waveDirector;
        [SerializeField] Text waveText;
        [SerializeField] Text moneyText;
        [SerializeField] Text messageText;
        [SerializeField] GameObject northWaveWarning;
        [SerializeField] GameObject eastWaveWarning;
        [SerializeField] GameObject southWaveWarning;
        [SerializeField] GameObject westWaveWarning;

        [Header("Game Over References")]
        [SerializeField] GameObject gameOverPanel;
        [SerializeField] Button restartButton;

        [Header("Attributes")]
        [SerializeField] UState<Color> powerCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> radarCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> turretCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> repairCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> powerLinkColour = new(Color.cyan);
        [SerializeField] UState<Color> unitRingColour = new(Color.cyan);
        [SerializeField] UState<Color> validPlacementColour = new(Color.green);
        [SerializeField] UState<Color> invalidPlacementColour = new(Color.red);
        [SerializeField] float waveWarningBlinkTime = 0.5f;   // seconds per on/off phase

        public State<Building> Placing { get; } = new();

        Trigger onLeftClick = Trigger.Create();
        Trigger onRightClick = Trigger.Create();

        protected override void Init(EffectBuilder s) {
            messageText.text = "";
            pregamePanel.SetActive(false);
            gameplayPanel.SetActive(false);
            gameOverPanel.SetActive(false);
            northWaveWarning.SetActive(false);
            eastWaveWarning.SetActive(false);
            southWaveWarning.SetActive(false);
            westWaveWarning.SetActive(false);

            var isPregame = s.Memo(s => s.D(GameState.Mode) == GameMode.Pregame);
            var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);
            var isGameOver = s.Memo(s => s.D(GameState.Mode) == GameMode.GameOver);

            s.Phase(isPregame, s => {
                pregamePanel.SetActive(true);
                s.OnCleanup(() => pregamePanel.SetActive(false));

                s.Subscribe(startButton.onClick, () => GameState.Mode.Set(GameMode.Playing));
            });

            s.Phase(isPlaying, s => {
                gameplayPanel.SetActive(true);
                s.OnCleanup(() => gameplayPanel.SetActive(false));
                s.OnCleanup(() => Placing.Set(null));

                s.Effect(s => moneyText.text = $"${s.D(GameState.Money)} (+{s.D(GameState.CollectRate)})");

                // Whole seconds derived from the ticking countdown, so the text only
                // rewrites when the displayed number changes.
                var countdown = s.Memo(s => Mathf.CeilToInt(s.D(waveDirector.NextWaveIn)));
                s.Effect(s => {
                    var direction = s.D(waveDirector.Front).ToString().ToLower();
                    waveText.text = s.D(waveDirector.IsAssaulting)
                        ? $"Wave {s.D(waveDirector.Wave)} — attacking from the {direction}"
                        : $"Wave {s.D(waveDirector.Wave) + 1} from the {direction} in {s.D(countdown)}s";
                });

                
                s.Effect(ShowWaveWarning);

                var hasMousePoint = s.Memo(s => {
                    var p = s.D(GameState.View).MousePoint;
                    return p != null && GameState.LevelBounds.Contains(p.Value);
                });

                s.Phase(hasMousePoint, s => {
                    var placingNow = s.D(Placing);
                    if (placingNow == null) s.Effect(ShowHovered);
                    else s.Effect(PlaceBuilding(placingNow));
                });
            });

            s.Phase(isGameOver, s => {
                gameOverPanel.SetActive(true);
                s.OnCleanup(() => gameOverPanel.SetActive(false));

                s.Subscribe(restartButton.onClick, () => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex));
            });
        }

        EffectBlock ShowHovered => s => {
            var sensor = s.Use(GameState.GroundZone.AddSensor(() => new Circle(GameState.View.Now.MousePoint.Value, 0f)));
            var hovered = s.Memo(s => sensor.Overlaps.Count > 0 ? sensor.Overlaps[0] : null, sensor.OverlapsChanged);

            s.Effect(s => {
                var hoveredNow = s.D(hovered);
                var hoverable = hoveredNow?.Owner.GetComponent<IHoverable>();
                if (hoverable == null) return;

                var description = s.Memo(s => s.D(hoverable.HoverInfo).Description);
                s.Effect(s => {
                    messageText.text = s.D(description);
                    s.OnCleanup(() => messageText.text = "");
                });

                var coverage = s.Memo(s => s.D(hoverable.HoverInfo).Coverage);
                s.Effect(s => {
                    switch (s.D(coverage)) {
                        case CoverageType.Power: s.Effect(CoverageDisplay.Draw(GameState.PowerZone, powerCoverageColour, body => body.IsProvider)); break;
                        case CoverageType.Radar: s.Effect(CoverageDisplay.Draw(GameState.RadarZone, radarCoverageColour)); break;
                        case CoverageType.Turret: s.Effect(CoverageDisplay.Draw(GameState.TurretZone, turretCoverageColour)); break;
                    case CoverageType.Repair: s.Effect(CoverageDisplay.Draw(GameState.RepairZone, repairCoverageColour)); break;
                    }
                });

                var powerNode = s.Memo(s => s.D(hoverable.HoverInfo).PowerNode);
                s.Effect(s => {
                    var node = s.D(powerNode);
                    if (node != null) s.Effect(LinkDisplay.Draw(node, powerLinkColour));
                });

                // Highlight ring, slightly larger than the unit's footprint.
                var circle = hoveredNow.Circle;
                s.Effect(CoverageDisplay.Draw(new Circle(circle.Center, circle.Radius * 1.5f), unitRingColour));
            });
        };

        // The placement experience: power coverage shows while choosing a spot, and the building's
        // footprint follows the mouse — recoloured by whether it can go there (touching provider
        // coverage, clear of other units). A click on a valid spot buys and places the building.
        EffectBlock PlaceBuilding(Building prefab) => s => {
            messageText.text = $"Placing {prefab.DisplayName} — press Escape to cancel";
            s.OnCleanup(() => messageText.text = "");

            s.Effect(CoverageDisplay.Draw(GameState.PowerZone, powerCoverageColour, body => body.IsProvider));

            var mousePos = s.Memo(s => s.D(GameState.View).MousePoint.Value);
            var footprint = s.Memo(s => new Circle(s.D(mousePos), prefab.Radius));

            var groundSensor = s.Use(GameState.GroundZone.AddSensor(() => footprint.Now));
            var powerSensor = s.Use(GameState.PowerZone.AddSensor(() => new Circle(mousePos.Now, 0f), body => body.IsProvider));
            var isValid = s.Memo(s => groundSensor.Overlaps.Count == 0 && powerSensor.Overlaps.Count > 0,
                groundSensor.OverlapsChanged, powerSensor.OverlapsChanged);
            var colour = s.Memo(s => s.D(isValid) ? s.D(validPlacementColour) : s.D(invalidPlacementColour));
            
            s.Effect(CoverageDisplay.Draw(footprint, colour));

            s.Subscribe(onLeftClick, () => {
                if (!isValid.Now) return;
                Pool.Spawn(prefab.gameObject, new Vector3(mousePos.Now.x, prefab.transform.position.y, mousePos.Now.z), Quaternion.identity);
                GameState.Money.Update(x => x - prefab.Cost);
                Placing.Set(null);
            });

            s.Subscribe(onRightClick, () => Placing.Set(null));
        };

        // Blink along the threatened screen edge while the wave is incoming.
        EffectBlock ShowWaveWarning => s => {
            if (s.D(waveDirector.IsAssaulting)) return;
            var bar = s.D(waveDirector.Front) switch {
                Edge.North => northWaveWarning,
                Edge.East => eastWaveWarning,
                Edge.South => southWaveWarning,
                _ => westWaveWarning,
            };

            IEnumerator onUpdate() {
                bar.SetActive(true);
                var timer = 0f;
                while (true) {
                    yield return null;
                    timer += Time.unscaledDeltaTime;
                    if (timer >= waveWarningBlinkTime) {
                        timer = 0f;
                        bar.SetActive(!bar.activeSelf);
                    }
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => {
                StopCoroutine(routine);
                bar.SetActive(false);
            });
        };

        void Update() {
            if (Input.GetMouseButtonDown(0)) onLeftClick.Invoke();
            if (Input.GetMouseButtonDown(1)) onRightClick.Invoke();
        }
    }
}
