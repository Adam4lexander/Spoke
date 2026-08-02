using Godot;

namespace Spoke.Examples.BaseDefence;

// The in-game panel's controls, wired in Gameplay.tscn.
public partial class GameplayPanel : Control {

    [Export] public RichTextLabel WaveText { get; set; }
    [Export] public RichTextLabel MoneyText { get; set; }
    [Export] public Label ResourcesText { get; set; }
    [Export] public Label MessageText { get; set; }

    /// <summary>One per buildable, in the same order as the sidebar's BuildItems.</summary>
    [Export] public Godot.Collections.Array<Button> BuildButtons { get; set; } = new();
}
