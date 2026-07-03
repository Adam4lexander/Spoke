using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Building : SpokeBehaviour {

        static readonly List<Building> all = new();
        public static ReadOnlyList<Building> All => new(all);

        [Header("References")]
        [SerializeField] Health health;
        [SerializeField] HealthBar healthBar;
        [SerializeField] MeshFX meshFX;
        [SerializeField] PowerNode powerNode;

        [Header("Attributes")]
        [SerializeField] int cost;
        [SerializeField] float radius = 0.6f;
        [SerializeField] UState<float> unpoweredDim = new(0.35f);

        public int Cost => cost;
        public float Radius => radius;
        public PowerNode Power => powerNode;

        protected override void Init(EffectBuilder s) {
            s.Effect(s => {
                var showHealth = s.D(health.IsAlive) && s.D(health.HPFraction) < 1f;
                healthBar.gameObject.SetActive(showHealth);
                healthBar.Fraction.Set(s.D(health.HPFraction));
            });

            s.Phase(IsEnabled, s => {
                s.Phase(health.IsAlive, s => {
                    all.Add(this);
                    s.OnCleanup(() => all.Remove(this));

                    // Physical footprint for hover-picking and blast damage (distinct from the network
                    // receiver the PowerNode registers in the power world).
                    s.Use(GameState.GroundZone.AddCollider(gameObject, () => new Circle(transform.position, radius)));

                    s.Subscribe(health.Damaged, () => meshFX.Blink(Color.red));
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
                        if (s.D(meshFX.IsShattered)) Pool.Despawn(gameObject);
                    });
                });
            });

            s.Effect(s => {
                if (s.D(powerNode.HasPower)) {
                    meshFX.SetTint(Color.white);
                    return;
                }
                var d = s.D(unpoweredDim);
                meshFX.SetTint(new Color(d, d, d, 1f));
            });
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.cyan);
        }
    }
}
