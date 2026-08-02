using Godot;

namespace Spoke.Examples.BaseDefence;

// The unit: Godot's answer to Unity's GameObject. Components hang off it, and it's the handle
// other systems are given when a collider hits one.
//
// It has no behaviour and nothing inherits from it. It exists because Godot has no GetComponent,
// so the two things a system can be handed a unit and need — what to damage, and what describes
// it — have to be wired in the scene instead of looked up.
public partial class Unit : Node2D {

    /// <summary>What a blast damages and a repair station heals. Unset on resource sites.</summary>
    [Export] public Health Health { get; set; }

    /// <summary>The component implementing IHoverable, which names this unit when the pointer
    /// hovers it. Unset on enemies.</summary>
    [Export] public Node Hoverable { get; set; }
}
