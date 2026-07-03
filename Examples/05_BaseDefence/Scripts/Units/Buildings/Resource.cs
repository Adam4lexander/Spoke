using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

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

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            var remaining = State.Create(startResources);

            s.Effect(s => {
                var left = s.D(remaining);
                var description = left > 0
                    ? $"Resource — generates money while powered ({left} left)"
                    : "Resource — depleted";
                hoverInfo.Set(new HoverInfo(description, CoverageType.None, powerNode));
            });

            s.Effect(s => {
                var frac = (float)s.D(remaining) / startResources;
                healthBar.gameObject.SetActive(frac < 1f && frac > 0f);
                healthBar.Fraction.Set(frac);
            });

            harvestFX.SetActive(false);
            s.Phase(IsEnabled, s => {
                // Physical footprint so the ground world can hover-pick this resource. It carries no
                // Health, so a blast query finds it here but has nothing to damage.
                s.Use(GameState.GroundZone.AddCollider(gameObject, () => new Circle(transform.position, radius)));

                var hasResources = s.Memo(s => s.D(remaining) > 0);
                var canHarvest = s.Memo(s => s.D(hasResources) && s.D(powerNode.HasPower));
                s.Phase(canHarvest, Harvest(remaining));

                // Spent: the crystals shatter away, leaving the mound as a permanent husk.
                var isDepleted = s.Memo(s => !s.D(hasResources));
                s.Phase(isDepleted, s => {
                    meshFX.Shatter();
                    s.OnCleanup(meshFX.Restore);
                });
            });
        }

        EffectBlock Harvest(State<int> remaining) => s => {
            harvestFX.SetActive(true);
            s.OnCleanup(() => harvestFX.SetActive(false));

            GameState.CollectRate.Update(x => x + 1);
            s.OnCleanup(() => GameState.CollectRate.Update(x => x - 1));

            IEnumerator onUpdate() {
                var timer = 0f;
                while (true) {
                    timer += Time.deltaTime;
                    if (timer > collectTime) {
                        timer = 0f;
                        GameState.Money.Update(x => x + 1);
                        remaining.Update(x => x - 1);
                    }
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        void OnDrawGizmosSelected() {
            new Circle(transform.position, radius).DrawGizmo(Color.cyan);
        }
    }
}