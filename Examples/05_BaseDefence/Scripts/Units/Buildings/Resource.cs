using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Resource : SpokeBehaviour, IHoverable {

        [Header("References")]
        [SerializeField] GameObject harvestFX;
        [SerializeField] PowerNode powerNode;

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] float collectTime = 2f;

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo("Resource — generates money while powered", CoverageType.None, powerNode));

            harvestFX.SetActive(false);
            s.Phase(IsEnabled, s => {
                // Physical footprint so the ground world can hover-pick this resource. It carries no
                // Health, so a blast query finds it here but has nothing to damage.
                s.Use(GameState.GroundZone.AddCollider(gameObject, () => new Circle(transform.position, radius)));

                s.Phase(powerNode.HasPower, Harvest);
            });
        }

        EffectBlock Harvest => s => {
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