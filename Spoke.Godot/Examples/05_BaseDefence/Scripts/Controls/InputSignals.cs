using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

// Publishes input events as triggers, so components subscribe to the events
// they care about while mounted instead of polling Input themselves.
public partial class InputSignals : SpokeNode {

    static InputSignals instance;

    readonly Trigger leftClick = Trigger.Create();
    readonly Trigger rightClick = Trigger.Create();
    readonly Dictionary<Key, Trigger> keyDowns = new();

    /// <summary>Fires when the left mouse button goes down over the board.</summary>
    public static ITrigger LeftClick => instance.leftClick;
    /// <summary>Fires when the right mouse button goes down over the board.</summary>
    public static ITrigger RightClick => instance.rightClick;

    /// <summary>Gets (and lazily creates) the trigger that fires when the given key goes down.</summary>
    public static ITrigger KeyDown(Key key) {
        if (!instance.keyDowns.TryGetValue(key, out var trigger)) instance.keyDowns[key] = trigger = Trigger.Create();
        return trigger;
    }

    protected override void Init(EffectBuilder s) {
        instance = this;

        // Input stops reaching a paused node, so hotkeys would die with the board without this.
        ProcessMode = ProcessModeEnum.Always;
    }

    // _UnhandledInput only sees events the UI didn't consume, so a click on a sidebar
    // button never reaches the board.
    public override void _UnhandledInput(InputEvent @event) {
        switch (@event) {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                leftClick.Invoke();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                rightClick.Invoke();
                break;
            case InputEventKey { Pressed: true, Echo: false } key when keyDowns.TryGetValue(key.Keycode, out var trigger):
                trigger.Invoke();
                break;
        }
    }
}
