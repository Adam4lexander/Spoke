using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Resource : SpokeBehaviour {

        [Header("References")]
        [SerializeField] GameObject harvestFX;
        [SerializeField] PowerNode powerNode;

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;

        protected override void Init(EffectBuilder s) {
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
        };

        void OnDrawGizmosSelected() {
            new Circle(transform.position, radius).DrawGizmo(Color.cyan);
        }
    }
}