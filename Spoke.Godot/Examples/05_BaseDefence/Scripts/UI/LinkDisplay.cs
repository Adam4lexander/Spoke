using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

// Renders a set of line segments. Like CoverageDisplay it creates and owns its own overlay node,
// torn down on cleanup, and redraws whenever the segments change.
public static class LinkDisplay {

    // Shows the node's parent chain up to the root: a line from each node to the provider
    // powering it. Re-walks whenever any parent along the chain changes.
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

    // Shows every node's link to the provider powering it: the grid's whole spanning tree.
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
