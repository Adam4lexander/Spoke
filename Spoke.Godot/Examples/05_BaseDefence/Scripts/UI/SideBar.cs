using Godot;

namespace Spoke.Examples.BaseDefence;

// The sidebar routes the game mode to one of four panels. Each panel is its own scene, so its
// layout is authored in the editor; the block only decides when it exists and what it says.
public partial class SideBar : SpokeControl {

    [ExportGroup("References")]
    [Export] public BoardInteractions Interactions { get; set; }
    [Export] public Godot.Collections.Array<BuildItem> BuildItems { get; set; } = new();

    [ExportGroup("Panels")]
    [Export] public PackedScene PregameScene { get; set; }
    [Export] public PackedScene GameplayScene { get; set; }
    [Export] public PackedScene EndScreenScene { get; set; }

    protected override void Init(EffectBuilder s) {
        var host = GetNode<Control>("Margin");

        s.Effect(s => {
            switch (s.D(GameState.Mode)) {
                case GameMode.Pregame:
                    s.Effect("Pregame", Pregame(host));
                    break;
                case GameMode.Playing:
                    s.Effect("Gameplay", Gameplay(host));
                    break;
                case GameMode.GameOver:
                    s.Effect("GameOver", EndScreen(host, "DEFEATED", Palette.Danger,
                        "Core building was destroyed"));
                    break;
                default:
                    s.Effect("Victory", EndScreen(host, "VICTORY", Palette.Healthy,
                        "All resource sites were harvested"));
                    break;
            }
        });
    }

    EffectBlock Pregame(Node host) => s => {
        var panel = s.Own(host, PregameScene.Instantiate<PregamePanel>());
        s.Subscribe(panel.PlayButton, Button.SignalName.Pressed, () => GameState.Mode.Set(GameMode.Playing));
    };

    EffectBlock Gameplay(Node host) => s => {
        var panel = s.Own(host, GameplayScene.Instantiate<GameplayPanel>());

        s.Effect(s => {
            var wave = s.D(GameState.Director.Wave);
            var header = $"[b]Wave {wave.Number}[/b]";
            var direction = wave.Front.ToString();
            if (wave.IsAssaulting) {
                panel.WaveText.Text = $"{header}\n[color=#{Palette.Danger.ToHtml(false)}][b]{direction} attacking[/b][/color]";
            } else {
                var colour = CountdownColour(wave.StartsIn).ToHtml(false);
                var where = wave.Front == WaveFront.None ? "" : $"{direction} ";
                panel.WaveText.Text = $"{header}\n{where}in [color=#{colour}]{wave.StartsIn}s[/color]";
            }
        });

        s.Effect(s => {
            var money = $"${Mathf.FloorToInt(s.D(GameState.Money))} (+{s.D(GameState.CollectRate):0.#})";
            panel.MoneyText.Text = s.D(GameState.Director.Wave).IsAssaulting
                ? $"{money}\n[font_size=10][color=#{Palette.Amber.ToHtml(false)}]harvesting paused[/color][/font_size]"
                : money;
        });

        s.Effect(s => panel.ResourcesText.Text = $"Resource Sites: {s.D(GameState.ResourcesRemaining)}");

        // The message line: placement instructions take priority, then the hovered unit's description.
        s.Effect(s => {
            var placing = s.D(Interactions.Placing);
            var hovered = s.D(Interactions.Hovering);
            if (placing != null) panel.MessageText.Text = $"Placing {placing.DisplayName} — press Escape to cancel";
            else if (hovered != null) panel.MessageText.Text = s.D(hovered.HoverInfo).Description;
            else panel.MessageText.Text = "";
        });

        // The buttons are authored in the panel and pair with BuildItems in order.
        var count = Mathf.Min(BuildItems.Count, panel.BuildButtons.Count);
        for (var i = 0; i < count; i++) ControlBuildItem(s, BuildItems[i], panel.BuildButtons[i]);
    };

    EffectBlock EndScreen(Node host, string heading, Color colour, string body) => s => {
        var panel = s.Own(host, EndScreenScene.Instantiate<EndScreenPanel>());
        panel.Heading.Text = heading;
        panel.Heading.AddThemeColorOverride("font_color", colour);
        panel.Body.Text = body;
        s.Subscribe(panel.RestartButton, Button.SignalName.Pressed, GameState.Restart);
    };

    void ControlBuildItem(EffectBuilder s, BuildItem item, Button button) {
        var idleLabel = $"{item.DisplayName} ({item.Hotkey}) - ${item.Cost}";
        button.Text = idleLabel;

        var canAfford = s.Memo(s => item.Cost <= s.D(GameState.Money));
        var isPlacing = s.Memo(s => s.D(Interactions.Placing) != null);
        var isNotPlacing = s.Memo(s => !s.D(isPlacing));

        s.Phase(isNotPlacing, s => {
            s.Effect(s => button.Disabled = !s.D(canAfford));

            void beginPlacing() {
                if (canAfford.Now) Interactions.Placing.Set(item);
            }
            s.Subscribe(button, Button.SignalName.Pressed, beginPlacing);
            s.Subscribe(InputSignals.KeyDown(item.Hotkey), beginPlacing);
        });

        s.Phase(isPlacing, s => {
            var isPlacingThis = s.Memo(s => s.D(Interactions.Placing) == item);

            // Only the selected button stays live, becoming the cancel affordance.
            s.Effect(s => button.Disabled = !s.D(isPlacingThis));

            s.Phase(isPlacingThis, s => {
                button.Text = $"Cancel ({item.Hotkey})";
                s.OnCleanup(() => button.Text = idleLabel);

                void cancel() => Interactions.Placing.Set(null);
                s.Subscribe(button, Button.SignalName.Pressed, cancel);
                s.Subscribe(InputSignals.KeyDown(Key.Escape), cancel);
            });
        });
    }

    // Pale blue far out, sliding through amber to red as the wave gets close.
    static Color CountdownColour(int startsIn) {
        var lull = GameState.Director.LullDuration;
        var closeness = lull > 0f ? Mathf.Clamp(1f - startsIn / lull, 0f, 1f) : 1f;
        return closeness < 0.5f
            ? Palette.PaleBlue.Lerp(Palette.Amber, closeness / 0.5f)
            : Palette.Amber.Lerp(Palette.Danger, (closeness - 0.5f) / 0.5f);
    }
}
