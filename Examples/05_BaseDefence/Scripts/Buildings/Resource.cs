using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Resource : SpokeBehaviour {

        [Header("References")]
        [SerializeField] GameObject harvestFX;
        [SerializeField] PowerNode powerNode;

        protected override void Init(EffectBuilder s) {
            harvestFX.SetActive(false);
            s.Phase(IsEnabled, s => {
                s.Phase(powerNode.HasPower, Harvest);
            });
        }

        EffectBlock Harvest => s => {
            harvestFX.SetActive(true);
            s.OnCleanup(() => harvestFX.SetActive(false));
        };
    }
}