using Godot;

namespace Spoke.Examples.BaseDefence;

// The unit root: Godot's answer to Unity's GameObject. Components hang off it as child nodes, and
// it's the handle other systems are given when a collider hits one. Godot has no GetComponent, so
// what a system needs from a unit is wired in the scene instead of looked up.
public partial class Unit : Node2D {

    /// <summary>What a blast damages and a repair station heals.</summary>
    [Export] public Health Health { get; set; }

    /// <summary>What names this unit when the pointer hovers it.</summary>
    [Export] public Node Hoverable { get; set; }
}
