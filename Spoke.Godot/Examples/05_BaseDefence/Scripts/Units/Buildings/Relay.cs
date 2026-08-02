using Godot;

namespace Spoke.Examples.BaseDefence;

// A building that extends the power network's reach. The relaying is handled by its PowerNode;
// this component only supplies the hover info.
public partial class Relay : SpokeNode, IHoverable {

    [Export] public Building Building { get; set; }

    readonly State<HoverInfo> hoverInfo = State.Create(default(HoverInfo));
    public ISignal<HoverInfo> HoverInfo => hoverInfo;

    protected override void Init(EffectBuilder s) {
        hoverInfo.Set(new HoverInfo(
            $"{Building.DisplayName.ToUpper()}\n\n" +
            "Extends the power grid, relaying power to any building inside its coverage.\n\n" +
            "Buildings lose power when their path to the Core is broken.",
            CoverageType.Power, Building.Power));
    }
}
