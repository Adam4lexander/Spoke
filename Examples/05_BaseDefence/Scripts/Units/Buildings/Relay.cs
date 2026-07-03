using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // A building that extends the power network's reach.
    public class Relay : SpokeBehaviour, IHoverable {

        [Header("References")]
        [SerializeField] Building building;

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo("Relay — extends power to nearby buildings", CoverageType.Power, building.Power));
        }
    }
}
