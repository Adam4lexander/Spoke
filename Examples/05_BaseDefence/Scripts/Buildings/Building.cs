using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Building : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] float maxHp = 5;
        [SerializeField] bool isCore = false;

        public bool IsCore => isCore;

        State<bool> hasService = new(false);
        public ISignal<bool> HasService => hasService;

        protected override void Init(EffectBuilder s) {
            s.Effect(ControlHasService);
        }

        // A building has service while any active service zone covers it. The service
        // network decides which zones are live (see Service); a building just consumes.
        // The core reads as serviced too, since its own always-on zone covers it.
        EffectBlock ControlHasService => s => {
            var watch = s.Use(GameState.Instance.ServiceZone.Watch(new Circle(transform.position, radius)));
            s.Effect(s => hasService.Set(s.D(watch.Items).Count > 0));
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.cyan);
        }
    }
}