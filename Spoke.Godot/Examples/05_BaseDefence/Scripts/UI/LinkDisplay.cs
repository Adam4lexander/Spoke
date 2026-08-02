using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// Draws the power grid as lines from each node to whatever is powering it. Like CoverageDisplay,
/// the overlay is created by the block that wants it and taken away when that block ends.
/// </summary>
public static class LinkDisplay {

    /// <summary>One node's chain up to the root. Re-walks whenever any parent along it changes.</summary>
    public static EffectBlock Draw(PowerNode start, Color colour) => s => {
        var segments = s.Memo(s => {
            var list = new List<(Vector2 from, Vector2 to)>();
            for (var node = start; node != null;) {
                var parent = s.D(node.Parent);
                if (parent == null) break;
                list.Add((node.Unit.GlobalPosition, parent.Unit.GlobalPosition));
                node = parent;
            }
            return list;
        });
        s.Effect(DrawSegments(segments, colour));
    };

    /// <summary>Every node's link to its provider: the grid's whole spanning tree.</summary>
    public static EffectBlock DrawAll(Color colour) => s => {
        var segments = s.Memo(s => {
            var list = new List<(Vector2 from, Vector2 to)>();
            foreach (var node in s.D(PowerNode.All)) {
                var parent = s.D(node.Parent);
                if (parent == null) continue;
                list.Add((node.Unit.GlobalPosition, parent.Unit.GlobalPosition));
            }
            return list;
        });
        s.Effect(DrawSegments(segments, colour));
    };

    static EffectBlock DrawSegments(ISignal<List<(Vector2 from, Vector2 to)>> segments, Color colour) => s => {
        var layer = s.Own(GameState.Board, new DrawLayer(12));
        s.Effect(s => {
            var list = s.D(segments);
            layer.OnDraw = l => {
                foreach (var (from, to) in list) l.DrawLine(from, to, colour, 1.5f, true);
            };
            layer.Refresh();
        });
    };
}
