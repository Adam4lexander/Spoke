using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Building : SpokeBehaviour {

        static readonly List<Building> all = new();
        public static IReadOnlyList<Building> All => all;

        [Header("References")]
        [SerializeField] Health health;
        [SerializeField] MeshFX meshFX;

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] bool isCore = false;
        [SerializeField] UState<float> unservicedDim = new(0.35f);

        public bool IsCore => isCore;
        public Health Health => health;

        public State<Service> Parent { get; } = new();

        State<bool> hasService = new(false);
        public ISignal<bool> HasService => hasService;

        State<Vector3> position = new();
        public ISignal<Vector3> Position => position;

        protected override void Init(EffectBuilder s) {
            position.Set(transform.position);

            s.Effect(WatchParent);
            s.Effect(WatchHasService);

            s.Phase(health.IsAlive, s => {
                all.Add(this);
                s.OnCleanup(() => all.Remove(this));

                var body = s.Use(GameState.BuildingZone.AddCollider(this, new Circle(Position.Now, radius)));
                s.Effect(s => body.Circle = new Circle(s.D(Position), radius));

                s.Subscribe(health.Damaged, () => meshFX.Blink(Color.red));
            });

            s.Effect(s => {
                if (s.D(hasService)) {
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

        EffectBlock WatchParent => s => {
            var parentNow = s.D(Parent);
            if (parentNow == null) return;

            var sensor = s.Use(GameState.ServiceZone.AddSensor(new Circle(Position.Now, radius)));
            s.Effect(s => sensor.Circle = new Circle(s.D(Position), radius));

            s.Reaction(s => {
                foreach (var c in sensor.Overlaps) {
                    if (c.Owner == parentNow) return;
                }
                Parent.Set(null);
            }, sensor.OverlapsChanged);

            s.Effect(s => {
                if (!s.D(parentNow.Building.hasService)) Parent.Set(null);
            });
        };

        EffectBlock WatchHasService => s => {
            const float powerDelay = 0.15f;
            var nextHasService = s.Memo(s => isCore || s.D(Parent) != null);
            var shouldChange = s.Memo(s => s.D(nextHasService) != s.D(hasService));
            s.Phase(shouldChange, s => {
                IEnumerator settle() {
                    yield return new WaitForSeconds(powerDelay);
                    hasService.Set(nextHasService.Now);
                }
                var routine = StartCoroutine(settle());
                s.OnCleanup(() => StopCoroutine(routine));
            });
        };

        void Update() {
            position.Set(transform.position);
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.cyan);
        }
    }
}
