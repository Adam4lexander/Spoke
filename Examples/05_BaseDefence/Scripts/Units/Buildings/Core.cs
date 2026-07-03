using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // The base's root building — the origin of the power network.
    public class Core : SpokeBehaviour, IHoverable {

        [Header("References")]
        [SerializeField] Building building;

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo("Core — the source of the base's power", CoverageType.Power, building.Power));
        }
    }
}
