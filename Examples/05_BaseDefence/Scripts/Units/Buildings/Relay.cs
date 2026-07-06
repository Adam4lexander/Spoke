using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // A building that extends the power network's reach.
    public class Relay : SpokeBehaviour, IHoverable {

        [Header("References")]
        [SerializeField] Building building;

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo(
                $"{building.DisplayName.ToUpper()}\n\n" +
                "Extends the power grid, relaying power to any building inside its coverage.\n\n" +
                "Buildings lose power when their path to the Core is broken.",
                CoverageType.Power, building.Power));
        }
    }
}
