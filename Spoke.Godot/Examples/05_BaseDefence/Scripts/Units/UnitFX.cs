using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// A unit's body, and the three things that ever happen to it: a persistent tint, a damage blink,
/// and a shatter-into-pieces death.
///
/// The pieces are this node's own children in the unit's scene — one Sprite2D per part of the
/// body — so what shatters is whatever the artist put there. A child named "Pivot" keeps the unit's
/// origin and can be rotated in place: a radar dish, a turret barrel.
///
/// Each command is a Trigger, handled by an effect docked for the duration of the animation. The
/// Dock is what makes "start a new blink, replacing any blink already running" a single line —
/// re-docking under the same key detaches the previous one, which unwinds its own cleanup.
/// </summary>
public partial class UnitFX : SpokeNode2D {

    // Straight from the Unity MeshFX prefab values.
    const float ShatterTime = 2f;
    const float BlinkTime = 0.15f;
    const float BlastSpeed = 2f;   // metres per second

    // Unity's shatter throws the pieces up and lets gravity bring them down. Top-down 2D has no
    // "up", so the pieces slide outward against drag and fade instead.
    const float Drag = 2.2f;

    readonly List<Node2D> pieces = new();
    readonly List<Vector2> homePositions = new();
    readonly List<float> homeRotations = new();

    readonly State<Color> tint = State.Create(Colors.White);
    readonly State<Color> flash = State.Create(new Color(1f, 1f, 1f, 0f));   // rgb = colour, a = blend amount
    readonly State<bool> isShattered = State.Create(false);

    readonly Trigger<Color> blinkCommand = Trigger.Create<Color>();
    readonly Trigger shatterCommand = Trigger.Create();
    readonly Trigger restoreCommand = Trigger.Create();

    /// <summary>The child named "Pivot", for units that rotate part of themselves. Null if there isn't one.</summary>
    public Node2D Pivot { get; private set; }

    /// <summary>True once the shatter has finished and the pieces are hidden.</summary>
    public ISignal<bool> IsShattered => isShattered;

    /// <summary>Sets a persistent colour multiplier on the body.</summary>
    public void SetTint(Color colour) => tint.Set(colour);

    /// <summary>Flashes the body to a colour, then fades back.</summary>
    public void Blink(Color colour) => blinkCommand.Invoke(colour);

    /// <summary>Blasts the body apart, then hides the pieces.</summary>
    public void Shatter() => shatterCommand.Invoke();

    /// <summary>Cancels everything in flight and puts the body back the way it started.</summary>
    public void Restore() => restoreCommand.Invoke();

    protected override void Init(EffectBuilder s) {
        foreach (var child in GetChildren()) {
            if (child is not Node2D piece) continue;
            pieces.Add(piece);
            homePositions.Add(piece.Position);
            homeRotations.Add(piece.Rotation);
            if (piece.Name == "Pivot") Pivot = piece;
        }

        // restoreCommand re-runs this block, which tears down any docked blink or shatter and
        // rebuilds from scratch. One trigger, and the whole animation state is gone.
        s.Effect(s => {
            s.Effect(ApplyColour);

            var dock = s.Dock();
            s.Subscribe(blinkCommand, colour => dock.Effect("blink", Blinking(colour)));
            s.Subscribe(shatterCommand, () => dock.Effect("shatter", Shattering));
        }, restoreCommand);
    }

    // SelfModulate, so the shatter below can fade the pieces with Modulate without the two fighting.
    EffectBlock ApplyColour => s => {
        s.Effect(s => {
            var t = s.D(tint);
            var f = s.D(flash);
            var target = new Color(f.R, f.G, f.B, 1f) * 1.4f;
            foreach (var piece in pieces) piece.SelfModulate = t.Lerp(target, f.A);
        });
    };

    EffectBlock Blinking(Color colour) => s => {
        var elapsed = 0.0;
        s.OnProcess(delta => {
            elapsed += delta;
            flash.Set(new Color(colour, Mathf.Max(0f, 1f - (float)(elapsed / BlinkTime))));
        });
        s.OnCleanup(() => flash.Set(new Color(1f, 1f, 1f, 0f)));
    };

    EffectBlock Shattering => s => {
        var velocity = new Vector2[pieces.Count];
        var spin = new float[pieces.Count];
        for (var i = 0; i < pieces.Count; i++) {
            // Blast outward from the body's centre, so it reads as an explosion rather than a drift.
            var outward = homePositions[i];
            if (outward.LengthSquared() < 0.01f) outward = Vector2.FromAngle(GD.Randf() * Mathf.Tau);
            var dir = (outward.Normalized() + Vector2.FromAngle(GD.Randf() * Mathf.Tau) * 0.35f).Normalized();
            velocity[i] = dir * World.Px(BlastSpeed) * (float)GD.RandRange(0.7, 1.3);
            spin[i] = (float)GD.RandRange(-6.0, 6.0);
        }

        var elapsed = 0.0;
        s.OnProcess(delta => {
            elapsed += delta;
            var fade = Mathf.Max(0f, 1f - (float)(elapsed / ShatterTime));
            for (var i = 0; i < pieces.Count; i++) {
                velocity[i] *= 1f - Mathf.Min(1f, Drag * (float)delta);
                pieces[i].Position += velocity[i] * (float)delta;
                pieces[i].Rotation += spin[i] * (float)delta;
                pieces[i].Modulate = new Color(1f, 1f, 1f, fade);
            }
        });

        // The unit isn't gone until the animation is; whoever is waiting to despawn it watches this.
        s.Wait(ShatterTime, () => {
            foreach (var piece in pieces) piece.Visible = false;
            isShattered.Set(true);
        });

        s.OnCleanup(() => {
            isShattered.Set(false);
            for (var i = 0; i < pieces.Count; i++) {
                pieces[i].Visible = true;
                pieces[i].Position = homePositions[i];
                pieces[i].Rotation = homeRotations[i];
                pieces[i].Modulate = Colors.White;
            }
        });
    };
}
