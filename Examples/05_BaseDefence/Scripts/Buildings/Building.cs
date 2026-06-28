using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Building : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] float maxHp = 5;
        [SerializeField] bool isCore = false;
        [SerializeField] UState<float> unservicedDim = new(0.35f);

        public bool IsCore => isCore;

        State<bool> hasService = new(false);
        public ISignal<bool> HasService => hasService;

        State<Vector3> position = new();
        public ISignal<Vector3> Position => position;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                s.Effect(WatchPosition);
                s.Effect(WatchHasService);
                s.Effect(DimWhenUnserviced);
            });
        }

        EffectBlock WatchPosition => s => {
            IEnumerator onUpdate() {
                while (true) {
                    position.Set(transform.position);
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        EffectBlock WatchHasService => s => {
            var sensor = s.Use(GameState.ServiceZone.AddSensor(new Circle(Position.Now, radius)));
            s.Effect(s => sensor.Circle = new Circle(s.D(Position), radius));
            var covered = s.Memo(s => sensor.Overlaps.Count > 0, sensor.Changed);
            s.Effect(s => hasService.Set(s.D(covered)));
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

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.cyan);
        }
    }
}