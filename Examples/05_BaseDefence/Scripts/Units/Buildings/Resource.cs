using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // A minable resource site. While powered and between waves it generates money and depletes;
    // once mined out it shatters. Clearing every site on the map is the win condition.
    public class Resource : SpokeBehaviour, IHoverable {

        [Header("References")]
        [SerializeField] GameObject harvestFX;
        [SerializeField] PowerNode powerNode;
        [SerializeField] HealthBar healthBar;
        [SerializeField] MeshFX meshFX;

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] float collectTime = 2f;
        [SerializeField] int startResources = 20;

        State<int> remaining = new();
        State<HoverInfo> hoverInfo = new();

        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            harvestFX.SetActive(false);

            s.Phase(IsEnabled, s => {
                remaining.Set(startResources);

                s.Effect(SyncHoverInfo);
                s.Effect(SyncHealthBar);

                s.Use(GameState.GroundZone.AddCollider(gameObject, () => new Circle(transform.position, radius)));

                var hasResources = s.Memo(s => s.D(remaining) > 0);
                var isDepleted = s.Memo(s => !s.D(hasResources));

                // A mined-out site keeps its place in the count until its shatter finishes.
                var standing = s.Memo(s => !s.D(meshFX.IsShattered));
                s.Phase(standing, s => {
                    GameState.ResourcesRemaining.Update(x => x + 1);
                    s.OnCleanup(() => GameState.ResourcesRemaining.Update(x => x - 1));
                });

                s.Phase(hasResources, s => {
                    // Income flows only between waves; an assault pauses every harvester.
                    var canHarvest = s.Memo(s => s.D(powerNode.HasPower) && !s.D(GameState.Director.Wave).IsAssaulting);
                    s.Phase(canHarvest, Harvest);
                });

                s.Phase(isDepleted, s => {
                    meshFX.Shatter();
                    s.OnCleanup(meshFX.Restore);
                });
            });
        }

        EffectBlock SyncHoverInfo => s => {
            var left = s.D(remaining);
            var description = left > 0
                ? $"RESOURCE\n\nGenerates ${1f / collectTime:0.##}/s while powered. Harvesting pauses during an attack.\n\n{left} remaining."
                : "RESOURCE\n\nDepleted.";
            hoverInfo.Set(new HoverInfo(description, CoverageType.None, powerNode));
        };

        EffectBlock SyncHealthBar => s => {
            var frac = (float)s.D(remaining) / startResources;
            healthBar.gameObject.SetActive(frac < 1f && frac > 0f);
            healthBar.Fraction.Set(frac);
        };

        EffectBlock Harvest => s => {
            harvestFX.SetActive(true);
            s.OnCleanup(() => harvestFX.SetActive(false));

            GameState.CollectRate.Update(x => x + 1f / collectTime);
            s.OnCleanup(() => GameState.CollectRate.Update(x => x - 1f / collectTime));

            var timer = 0f;
            s.Coroutine(() => {
                timer += Time.deltaTime;
                if (timer > collectTime) {
                    timer = 0f;
                    GameState.Money.Update(x => x + 1);
                    remaining.Update(x => x - 1);
                }
            });
        };

        void OnDrawGizmosSelected() {
            new Circle(transform.position, radius).DrawGizmo(Color.cyan);
        }
    }
}