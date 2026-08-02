using Godot;

namespace Spoke.Examples.BaseDefence;

// The briefing panel's controls, wired in Pregame.tscn.
public partial class PregamePanel : Control {
    [Export] public Button PlayButton { get; set; }
}
