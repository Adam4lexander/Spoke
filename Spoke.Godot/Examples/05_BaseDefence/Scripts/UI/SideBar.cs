using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// The sidebar. Routes the game mode to one of four panels, and each panel is a block: the controls
/// it needs exist while it's mounted, and are gone when it isn't.
///
/// Godot's UI is nodes, so building a panel and wiring a panel are the same act here. There's no
/// SetActive(true/false) anywhere in this file, and no per-panel root to remember to hide — the
/// Unity version needs both.
/// </summary>
public partial class SideBar : SpokeControl {

    protected override void Init(EffectBuilder s) {
        // The panel, its background and its column are scene structure — they exist for the whole
        // game. Only what changes with the mode is built by a block.
        var column = GetNode<VBoxContainer>("Margin/Column");

        // One effect, four panels. Changing mode disposes whichever panel is mounted — taking its
        // labels, its buttons and its signal connections with it — and mounts the next.
        s.Effect(s => {
            switch (s.D(GameState.Mode)) {
                case GameMode.Pregame:
                    s.Effect("Pregame", Pregame(column));
                    break;
                case GameMode.Playing:
                    s.Effect("Gameplay", Gameplay(column));
                    break;
                case GameMode.GameOver:
                    s.Effect("GameOver", EndScreen(column, "DEFEATED", Palette.Danger,
                        "The Core is gone, and the grid died with it."));
                    break;
                default:
                    s.Effect("Victory", EndScreen(column, "VICTORY", Palette.Healthy,
                        "Every resource site on the map is mined out."));
                    break;
            }
        });
    }

    EffectBlock Pregame(Node column) => s => {
        Heading(s, column, "BASE DEFENCE", Palette.PaleBlue);
        Body(s, column,
            "Your Core seeds a power grid. Buildings work only while a chain of relays connects " +
            "them back to it.\n\n" +
            "Harvest every resource site to win. Lose the Core and it's over.\n\n" +
            "WASD pans the camera.");

        Spacer(s, column, 10);
        var start = s.Own(column, MakeButton("Start", 20));
        s.Subscribe(start, Button.SignalName.Pressed, () => GameState.Mode.Set(GameMode.Playing));
    };

    EffectBlock Gameplay(Node column) => s => {
        var wave = s.Own(column, MakeLabel(14));       // waveText, Unity font size 14
        var money = s.Own(column, MakeLabel(18));      // moneyText, 18
        var resources = s.Own(column, MakeLabel(14));  // resourcesText, 14

        Spacer(s, column, 6);
        Body(s, column, "BUILD", Palette.Text, 12);

        foreach (var (spec, hotkey) in Units.Buildable) BuildItem(s, column, spec, hotkey);

        Spacer(s, column, 10);
        var message = s.Own(column, MakeLabel(14));    // messageText, 14
        message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        message.CustomMinimumSize = new Vector2(0, 140);
        message.VerticalAlignment = VerticalAlignment.Top;

        s.Effect(s => {
            var status = s.D(GameState.Director.Wave);
            if (status.IsAssaulting) {
                wave.Text = $"Wave {status.Number} — {status.Front} attacking";
                wave.AddThemeColorOverride("font_color", Palette.Danger);
            } else {
                var where = status.Front == WaveFront.None ? "" : $"{status.Front} ";
                wave.Text = $"Wave {status.Number} — {where}in {status.StartsIn}s";
                wave.AddThemeColorOverride("font_color", CountdownColour(status.StartsIn));
            }
        });

        s.Effect(s => {
            var amount = Mathf.FloorToInt(s.D(GameState.Money));
            var rate = s.D(GameState.CollectRate);
            var paused = s.D(GameState.Director.Wave).IsAssaulting;
            money.Text = paused ? $"${amount}   harvesting paused" : $"${amount}   +{rate:0.#}/s";
            money.AddThemeColorOverride("font_color", paused ? Palette.Amber : Palette.Text);
        });

        s.Effect(s => resources.Text = $"Resource sites left: {s.D(GameState.ResourcesRemaining)}");

        // Placement instructions take priority, then whatever the pointer is over.
        s.Effect(s => {
            var placing = s.D(GameState.Interactions.Placing);
            var hovered = s.D(GameState.Interactions.Hovering);
            if (placing != null) {
                message.Text = $"Placing {placing.DisplayName}.\n\nClick a spot inside power coverage, clear of other units. Right-click or Escape to cancel.";
            } else if (hovered != null) {
                message.Text = s.D(hovered.HoverInfo).Description;
            } else {
                message.Text = "";
            }
        });
    };

    // One build button and everything that drives it. A plain method, not a block: nothing here
    // re-runs, so there's nothing for an effect to re-run. The memos and phases attach to whichever
    // builder is passed in, and unmount with it.
    static void BuildItem(EffectBuilder s, Node column, UnitSpec spec, Key hotkey) {
        var idleLabel = $"{spec.DisplayName} ({hotkey}) — ${spec.Cost}";
        var button = s.Own(column, MakeButton(idleLabel, 15));
        var placing = GameState.Interactions.Placing;

        var canAfford = s.Memo(s => spec.Cost <= s.D(GameState.Money));
        var isPlacingAnything = s.Memo(s => s.D(placing) != null);
        var isPlacingThis = s.Memo(s => s.D(placing) == spec);
        var isIdle = s.Memo(s => !s.D(isPlacingAnything));

        s.Phase(isIdle, s => {
            s.Effect(s => button.Disabled = !s.D(canAfford));

            void begin() {
                if (canAfford.Now) placing.Set(spec);
            }
            s.Subscribe(button, Button.SignalName.Pressed, begin);
            s.Subscribe(InputSignals.KeyDown(hotkey), begin);
        });

        s.Phase(isPlacingAnything, s => {
            // Only the selected button stays live, and it becomes the cancel affordance.
            s.Effect(s => button.Disabled = !s.D(isPlacingThis));

            s.Phase(isPlacingThis, s => {
                button.Text = $"Cancel ({hotkey})";
                s.OnCleanup(() => button.Text = idleLabel);

                void cancel() => placing.Set(null);
                s.Subscribe(button, Button.SignalName.Pressed, cancel);
                s.Subscribe(InputSignals.KeyDown(hotkey), cancel);
            });
        });
    }

    EffectBlock EndScreen(Node column, string title, Color colour, string body) => s => {
        Heading(s, column, title, colour);
        Body(s, column, body);
        Spacer(s, column, 10);
        var restart = s.Own(column, MakeButton("Play again", 18));
        s.Subscribe(restart, Button.SignalName.Pressed, GameState.Restart);
    };

    // Pale blue far out, sliding through amber to red as the wave closes in.
    static Color CountdownColour(int startsIn) {
        var closeness = Mathf.Clamp(1f - startsIn / WaveDirector.LullDuration, 0f, 1f);
        return closeness < 0.5f
            ? Palette.PaleBlue.Lerp(Palette.Amber, closeness / 0.5f)
            : Palette.Amber.Lerp(Palette.Danger, (closeness - 0.5f) / 0.5f);
    }

    // ------------------------------------------------------------------ small builders

    static Label MakeLabel(int size, Color? colour = null) {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour ?? Palette.Text);
        return label;
    }

    static Button MakeButton(string text, int size) {
        var button = new Button { Text = text };
        button.AddThemeFontSizeOverride("font_size", size);
        return button;
    }

    static void Heading(EffectBuilder s, Node column, string text, Color colour) {
        var label = s.Own(column, MakeLabel(24, colour));
        label.Text = text;
    }

    static void Body(EffectBuilder s, Node column, string text, Color? colour = null, int size = 14) {
        var label = s.Own(column, MakeLabel(size, colour ?? Palette.Text));
        label.Text = text;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }

    static void Spacer(EffectBuilder s, Node column, int height)
        => s.Own(column, new Control { CustomMinimumSize = new Vector2(0, height) });
}
