using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Building : SpokeBehaviour {

        static readonly List<Building> all = new();
        public static IReadOnlyList<Building> All => all;

        [Header("References")]
        [SerializeField] Health health;
        [SerializeField] MeshFX meshFX;
        [SerializeField] PowerNode powerNode;

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] UState<float> unpoweredDim = new(0.35f);

        public PowerNode Power => powerNode;

        protected override void Init(EffectBuilder s) {
            s.Phase(health.IsAlive, s => {
                all.Add(this);
                s.OnCleanup(() => all.Remove(this));

                // Physical footprint for hover-picking and blast damage (distinct from the network
                // receiver the PowerNode registers in the power world).
                s.Use(GameState.GroundZone.AddCollider(gameObject, () => new Circle(transform.position, radius)));

                s.Subscribe(health.Damaged, () => meshFX.Blink(Color.red));
            });

            s.Effect(s => {
                if (s.D(powerNode.HasPower)) {
                    meshFX.SetTint(Color.white);
                    return;
                }
                var d = s.D(unpoweredDim);
                meshFX.SetTint(new Color(d, d, d, 1f));
            });

            var isDead = s.Memo(s => !s.D(health.IsAlive));
            s.Phase(isDead, s => {
                meshFX.Shatter();
                powerNode.enabled = false;
                s.OnCleanup(() => {
                    meshFX.Restore();
                    powerNode.enabled = true;
                });
                s.Effect(s => {
                    if (s.D(meshFX.IsShattered)) Destroy(gameObject);
                });
            });
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.cyan);
        }
    }
}
