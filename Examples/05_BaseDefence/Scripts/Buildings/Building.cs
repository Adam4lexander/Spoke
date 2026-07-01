using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Building : SpokeBehaviour {

        static readonly List<Building> all = new();
        public static IReadOnlyList<Building> All => all;

        [Header("References")]
        [SerializeField] Health health;
        [SerializeField] MeshFX meshFX;
        [SerializeField] Service service;

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] UState<float> unservicedDim = new(0.35f);

        public float Radius => radius;
        public Health Health => health;
        public Service Service => service;

        protected override void Init(EffectBuilder s) {
            s.Phase(health.IsAlive, s => {
                all.Add(this);
                s.OnCleanup(() => all.Remove(this));

                // Physical footprint for hover-picking and blast damage (distinct from the network
                // receiver the Service registers in the service world).
                s.Use(GameState.BuildingZone.AddCollider(this, () => new Circle(transform.position, radius)));

                s.Subscribe(health.Damaged, () => meshFX.Blink(Color.red));
            });

            s.Effect(s => {
                if (s.D(service.HasService)) {
                    meshFX.SetTint(Color.white);
                    return;
                }
                var d = s.D(unservicedDim);
                meshFX.SetTint(new Color(d, d, d, 1f));
            });

            var isDead = s.Memo(s => !s.D(health.IsAlive));
            s.Phase(isDead, s => {
                meshFX.Shatter();
                s.OnCleanup(meshFX.Restore);
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
