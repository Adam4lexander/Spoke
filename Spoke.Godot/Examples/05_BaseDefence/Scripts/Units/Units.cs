using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// The catalogue: one entry per kind of thing that can appear on the board.
///
/// The scenes themselves are [Export] PackedScene fields on GameState, wired up in
/// 05_BaseDefence.tscn — which is Godot's answer to Unity's prefab fields on a MonoBehaviour, and
/// means nothing here hardcodes a res:// path. Move the whole folder and it still works.
/// </summary>
public static class Units {

    public static UnitSpec Core { get; private set; }
    public static UnitSpec Relay { get; private set; }
    public static UnitSpec Radar { get; private set; }
    public static UnitSpec Turret { get; private set; }
    public static UnitSpec Repair { get; private set; }
    public static UnitSpec ResourceSite { get; private set; }
    public static UnitSpec Enemy1 { get; private set; }
    public static UnitSpec Enemy2 { get; private set; }
    public static UnitSpec Enemy3 { get; private set; }
    public static UnitSpec BombBlast { get; private set; }

    /// <summary>What the player can build, in sidebar order, with the key that selects it.</summary>
    public static (UnitSpec Spec, Key Hotkey)[] Buildable { get; private set; } = System.Array.Empty<(UnitSpec, Key)>();

    /// <summary>Called by GameState from its Init, which runs before any unit exists.</summary>
    internal static void Bind(GameState game) {
        Core = new UnitSpec(game.CoreScene);
        Relay = new UnitSpec(game.RelayScene);
        Radar = new UnitSpec(game.RadarScene);
        Turret = new UnitSpec(game.TurretScene);
        Repair = new UnitSpec(game.RepairScene);
        ResourceSite = new UnitSpec(game.ResourceSiteScene);
        Enemy1 = new UnitSpec(game.Enemy1Scene);
        Enemy2 = new UnitSpec(game.Enemy2Scene);
        Enemy3 = new UnitSpec(game.Enemy3Scene);
        BombBlast = new UnitSpec(game.BombBlastScene);

        // Order and hotkeys as serialized on the SideBar in BaseDefence.unity.
        Buildable = new[] {
            (Relay, Key.E),
            (Radar, Key.R),
            (Turret, Key.T),
            (Repair, Key.Y),
        };
    }
}
