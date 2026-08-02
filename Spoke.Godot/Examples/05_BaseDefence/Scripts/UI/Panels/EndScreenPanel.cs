using Godot;

namespace Spoke.Examples.BaseDefence;

// Shared by victory and defeat, which differ only in their heading and one line of body text.
public partial class EndScreenPanel : Control {
    [Export] public Label Heading { get; set; }
    [Export] public Label Body { get; set; }
    [Export] public Button RestartButton { get; set; }
}
