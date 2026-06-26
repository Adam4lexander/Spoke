using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Service : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Building building;

        [Header("Attributes")]
        [SerializeField] float range = 5f;

        protected override void Init(EffectBuilder s) {

            var providesService = s.Memo(s => s.D(IsEnabled) && s.D(building.HasService));
            
            s.Phase(providesService, s => {
                s.Use(GameState.Instance.ServiceZone.Add(this, new Circle(transform.position, range)));
            });
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, range);
            circle.DrawGizmo(Color.red);
        }
    }
}