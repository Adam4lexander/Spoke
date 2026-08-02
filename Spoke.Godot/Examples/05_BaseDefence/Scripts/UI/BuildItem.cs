using Godot;

namespace Spoke.Examples.BaseDefence;

// A buildable item in the sidebar: the prefab to place, its hotkey, and the coverage shown
// while placing. A PackedScene can't be read without instantiating it, so the name, price and
// footprint come from one throwaway instance, built once per kind.
[GlobalClass]
public partial class BuildItem : Resource {

    [Export] public PackedScene Prefab { get; set; }
    [Export] public Key Hotkey { get; set; }
    [Export] public CoverageType Coverage { get; set; }

    bool probed;
    string displayName = "";
    int cost;
    float radius;

    public string DisplayName { get { Probe(); return displayName; } }
    public int Cost { get { Probe(); return cost; } }
    public float Radius { get { Probe(); return radius; } }

    void Probe() {
        if (probed || Prefab == null) return;
        probed = true;
        var probe = Prefab.Instantiate<Node2D>();
        foreach (var child in probe.GetChildren()) {
            if (child is not Building building) continue;
            displayName = building.DisplayName;
            cost = building.Cost;
            radius = building.Radius;
            break;
        }
        probe.Free();
    }
}
