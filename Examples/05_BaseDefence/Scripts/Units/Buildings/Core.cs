using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // The base's root building — the origin of the power network.
    public class Core : SpokeBehaviour, IHoverable {

        [Header("References")]
        [SerializeField] Building building;

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo(
                $"{building.DisplayName.ToUpper()}\n\n" +
                "Seeds the power grid, all buildings must trace a path to it for receiving power.\n\n" +
                "Game is over if this building is destroyed.",
                CoverageType.Power, building.Power));
        }
    }
}
