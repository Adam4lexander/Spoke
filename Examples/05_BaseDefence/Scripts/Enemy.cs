using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Enemy : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Health health;
        [SerializeField] GameObject fireFrom;
        [SerializeField] GameObject showOnTracked;

        [Header("Attributes")]
        [SerializeField] float radius;

        State<Vector3> position = new();

        protected override void Init(EffectBuilder s) {
            position.Set(transform.position);

            var isTrackable = s.Memo(s => s.D(IsEnabled) && s.D(health.IsAlive));

            s.Phase(isTrackable, s => {
                var sensor = s.Use(GameState.RadarZone.Add(default, new Circle(position.Now, radius), detects: true, detectable: false));
                s.Effect(s => sensor.Circle = new Circle(s.D(position), radius));
                
                var isTracked = s.Memo(s => sensor.Overlaps.Count > 0, sensor.Changed);

                s.Phase(isTracked, s => {
                    showOnTracked.SetActive(true);
                    s.OnCleanup(() => showOnTracked.SetActive(false));

                    var collider = s.Use(GameState.TrackedEnemyZone.Add(this, new Circle(position.Now, radius)));
                    s.Effect(s => collider.Circle = new Circle(s.D(position), radius));
                });
            });
        }

        void Update() {
            position.Set(transform.position);
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.red);
        }
    }
}