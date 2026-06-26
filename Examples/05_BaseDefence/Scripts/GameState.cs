using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class GameState : SpokeSingleton<GameState> {

        public SpatialIndex<Service> ServiceZone { get; } = new();

        protected override void Init(EffectBuilder s) {
            
        }
    }
}