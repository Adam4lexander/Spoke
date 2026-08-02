using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>The states a playthrough moves through. Only Playing runs the simulation; the rest freeze it.</summary>
public enum GameMode { Pregame, Playing, GameOver, Victory }

// The game's central hub: a hand-placed singleton holding the shared state every system reads.
// It also owns the win/loss conditions (see Init).
//
// Godot scripts can't be generic, so there's no SpokeSingleton here -- it publishes itself from
// Init instead, which runs on entering the tree, before any descendant's.
public partial class GameState : SpokeNode2D {

    [ExportGroup("References")]
    [Export] public WaveDirector WaveDirector { get; set; }
    [Export] public CameraControls CameraControls { get; set; }
    [Export] public Core Core { get; set; }
    /// <summary>Everything in the world lives under here. Units are spawned into it by the Pool.</summary>
    [Export] public Node2D BoardRoot { get; set; }

    [ExportGroup("Attributes")]
    [Export] public float StartMoney { get; set; } = 100f;
    [Export] public Vector2 Dimensions { get; set; } = new(40f, 40f);

    static GameState instance;

    public static GameState Instance => instance;

    readonly State<GameMode> mode = State.Create(GameMode.Pregame);
    readonly State<float> money = State.Create(0f);
    readonly State<float> collectRate = State.Create(0f);
    readonly State<int> resourcesRemaining = State.Create(0);

    // One spatial world per query concern. Units register colliders and sensors and read overlaps;
    // all six are ticked once per frame, at the top of Init.
    readonly CollisionWorld<PowerBody> powerZone = new();
    readonly CollisionWorld<Unit> groundZone = new();
    readonly CollisionWorld<Radar> radarZone = new();
    readonly CollisionWorld<Turret> turretZone = new();
    readonly CollisionWorld<Repair> repairZone = new();
    readonly CollisionWorld<Enemy> enemyZone = new();

    public static IState<GameMode> Mode => instance.mode;
    public static IState<float> Money => instance.money;

    // Total money earned per second across all active harvesters.
    public static IState<float> CollectRate => instance.collectRate;

    // Resource sites on the map not yet mined out; victory when it reaches zero.
    public static IState<int> ResourcesRemaining => instance.resourcesRemaining;

    // The current camera view: what board the camera sees, and where the cursor points on it.
    public static ISignal<View> View => instance.CameraControls.View;

    public static WaveDirector Director => instance.WaveDirector;
    public static Node2D Board => instance.BoardRoot;

    public static Rect2 LevelBounds => BoundsOf(instance.Dimensions);

    // Static because Ground is a [Tool] script and draws the level in the editor, where there's
    // no live instance.
    public static Rect2 BoundsOf(Vector2 metres) {
        var size = metres * World.PixelsPerMetre;
        return new Rect2(-size * 0.5f, size);
    }

    public static CollisionWorld<PowerBody> PowerZone => instance.powerZone;
    public static CollisionWorld<Unit> GroundZone => instance.groundZone;
    public static CollisionWorld<Radar> RadarZone => instance.radarZone;
    public static CollisionWorld<Turret> TurretZone => instance.turretZone;
    public static CollisionWorld<Repair> RepairZone => instance.repairZone;
    public static CollisionWorld<Enemy> EnemyZone => instance.enemyZone;

    /// <summary>Reloads the active scene, restarting the game from Pregame.</summary>
    public static void Restart() => instance.GetTree().ReloadCurrentScene();

    protected override void Init(EffectBuilder s) {

        // _EnterTree propagates top-down, so the hub is published before any unit reaches for it.
        instance = this;

        // Every world is re-sampled once a frame, before anything reads it.
        s.OnProcess(_ => {
            powerZone.Tick();
            groundZone.Tick();
            radarZone.Tick();
            turretZone.Tick();
            repairZone.Tick();
            enemyZone.Tick();
        });

        // Idle pooled units have no parent, so nothing else will ever free them.
        s.OnCleanup(Pool.Clear);

        var isPlaying = s.Memo(s => s.D(mode) == GameMode.Playing);

        // Pause the sim unless we're in GameMode.Playing
        s.Effect(s => GetTree().Paused = !s.D(isPlaying));

        s.Phase(isPlaying, s => {
            money.Set(StartMoney);

            // Victory once every resource site on the map has been mined out.
            var allMined = s.Memo(s => s.D(resourcesRemaining) == 0);
            s.Phase(allMined, s => mode.Set(GameMode.Victory));

            // Game Over once the Core is gone. It leaves the field only after its death shatter finishes.
            var coreLost = s.Memo(s => !s.D(Core.IsInTree));
            s.Phase(coreLost, s => mode.Set(GameMode.GameOver));
        });
    }
}
