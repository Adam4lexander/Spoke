using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Building : SpokeBehaviour {

        static readonly List<Building> all = new();
        public static IReadOnlyList<Building> All => all;

        [Header("References")]
        [SerializeField] Health health;
        [SerializeField] MeshShatterFX shatterFX;

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] bool isCore = false;
        [SerializeField] UState<float> unservicedDim = new(0.35f);

        public bool IsCore => isCore;
        public Health Health => health;

        State<bool> hasService = new(false);
        public ISignal<bool> HasService => hasService;

        State<Vector3> position = new();
        public ISignal<Vector3> Position => position;

        protected override void Init(EffectBuilder s) {
            position.Set(transform.position);

            s.Phase(health.IsAlive, s => {
                all.Add(this);
                s.OnCleanup(() => all.Remove(this));

                var body = s.Use(GameState.BuildingZone.AddCollider(this, new Circle(Position.Now, radius)));
                s.Effect(s => body.Circle = new Circle(s.D(Position), radius));

                var inServiceCoverage = s.Effect(IsInServiceCoverage);
                s.Effect(s => {
                    hasService.Set(IsCore || s.D(inServiceCoverage) && s.D(health.IsAlive));
                });
                s.OnCleanup(() => hasService.Set(false));
            });
            
            s.Effect(DimWhenUnserviced);

            var isDead = s.Memo(s => !s.D(health.IsAlive));
            s.Phase(isDead, s => {
                shatterFX.StartFX();
                s.Effect(s => {
                    if (s.D(shatterFX.IsFinished)) Destroy(gameObject);
                });
            });
        }

        EffectBlock<bool> IsInServiceCoverage => s => {
            var coverageSensor = s.Use(GameState.ServiceZone.AddSensor(new Circle(Position.Now, radius)));
            s.Effect(s => coverageSensor.Circle = new Circle(s.D(Position), radius));

            return s.Memo(s => coverageSensor.Overlaps.Count > 0, coverageSensor.OverlapsChanged);
        };

        EffectBlock DimWhenUnserviced => s => {
            var renderers = GetComponentsInChildren<Renderer>();
            var block = new MaterialPropertyBlock();

            foreach (var renderer in renderers) {
                var litColour = renderer.sharedMaterial.color;
                s.Effect(s => {
                    if (s.D(hasService)) return;
                    var colour = litColour * s.D(unservicedDim);
                    colour.a = litColour.a;
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_Color", colour);
                    renderer.SetPropertyBlock(block);
                    s.OnCleanup(() => {
                        renderer.SetPropertyBlock(null);
                    });
                });
            }
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