using Godot;

namespace Spoke.Examples.BaseDefence;

// Reveals enemies within its coverage so turrets can target them. Runs only while powered.
public partial class Radar : SpokeNode, IHoverable {

    Node2D Unit => Building.Unit;

    [ExportGroup("References")]
    [Export] public Building Building { get; set; }
    [Export] public Node2D DishPivot { get; set; }

    [ExportGroup("Attributes")]
    [Export] public float Range { get; set; } = 8f;
    [Export] public float DishRotationSpeed { get; set; } = 90f;

    readonly State<HoverInfo> hoverInfo = State.Create(default(HoverInfo));
    public ISignal<HoverInfo> HoverInfo => hoverInfo;

    protected override void Init(EffectBuilder s) {
        hoverInfo.Set(new HoverInfo(
            $"{Building.DisplayName.ToUpper()}\n\n" +
            "Reveals enemies inside its coverage to turrets.\n\n" +
            "Turrets cannot fire at enemies that no radar has revealed.",
            CoverageType.Radar, Building.Power));

        var isRunning = s.Memo(s => s.D(IsInTree) && s.D(Building.Power.HasPower));

        s.Phase(isRunning, s => {
            s.Use(GameState.RadarZone.AddCollider(this, () => new Circle(Unit.GlobalPosition, World.Px(Range))));

            s.OnProcess(delta => DishPivot.Rotation += Mathf.DegToRad(DishRotationSpeed) * (float)delta);
        });
    }
}
