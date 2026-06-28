using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Enemy : SpokeBehaviour {

        [Header("References")]
        [SerializeField] GameObject fireFrom;
        [SerializeField] GameObject showOnTracked;

        [Header("Attributes")]
        [SerializeField] float radius;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                var position = s.Effect(WatchPosition);

                var sensor = s.Use(GameState.RadarZone.AddSensor(new Circle(position.Now, radius)));
                s.Effect(s => sensor.Area = new Circle(s.D(position), radius));
                var isTracked = s.Memo(s => sensor.Overlaps.Count > 0, sensor.Changed);

                s.Effect(s => showOnTracked.SetActive(s.D(isTracked)));
                s.Phase(isTracked, s => {
                    var collider = s.Use(GameState.TrackedEnemyZone.AddCollider(this, new Circle(position.Now, radius)));
                    s.Effect(s => collider.Circle = new Circle(s.D(position), radius));
                });
            });
        }

        EffectBlock<Vector3> WatchPosition => s => {
            var position = State.Create(transform.position);
            IEnumerator onUpdate() {
                while (true) {
                    position.Set(transform.position);
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
            return position;
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.red);
        }
    }
}