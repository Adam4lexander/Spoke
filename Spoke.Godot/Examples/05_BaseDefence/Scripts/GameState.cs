using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>The states a playthrough moves through. Only Playing runs the simulation; the rest freeze it.</summary>
public enum GameMode { Pregame, Playing, GameOver, Victory }

/// <summary>
/// The game's central hub, and the scene root. Holds the shared state every system reads, and owns
/// the win and loss conditions.
///
/// It does not build the game. The board, the camera, the UI, the Core and all 27 resource sites
/// are authored in 05_BaseDefence.tscn, the same way the Unity version authors them in
/// BaseDefence.unity — the starting state of a level is scene data, not a loop in C#. What this
/// class does is find those nodes and publish them.
///
/// Unity's version is a SpokeSingleton. Godot has no generic-script equivalent — Godot scripts
/// can't be generic — and its own answer, an autoload, would need a Project Settings entry in
/// whatever project imports this folder. So this is what the Unity version's comment describes
/// anyway: a hand-placed node that publishes itself from Init, which — because Init runs on
/// entering the tree — happens before any descendant’s. Every unit in the scene can read it.
/// </summary>
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

    /// <summary>The hub. Valid from the first descendant's Init onwards.</summary>
    public static GameState Instance => instance;

    readonly State<GameMode> mode = State.Create(GameMode.Pregame);
    readonly State<float> money = State.Create(0f);
    readonly State<float> collectRate = State.Create(0f);
    readonly State<int> resourcesRemaining = State.Create(0);

    // One spatial world per query concern. Units register colliders and sensors and read overlaps;
    // all six are ticked once per frame, at the top of Init.
    readonly CollisionWorld<PowerBody> powerZone = new();
    readonly CollisionWorld<Node2D> groundZone = new();
    readonly CollisionWorld<Radar> radarZone = new();
    readonly CollisionWorld<Turret> turretZone = new();
    readonly CollisionWorld<Repair> repairZone = new();
    readonly CollisionWorld<Enemy> enemyZone = new();

    public static IState<GameMode> Mode => instance.mode;
    public static IState<float> Money => instance.money;

    /// <summary>Total money earned per second across all active harvesters.</summary>
    public static IState<float> CollectRate => instance.collectRate;

    /// <summary>Resource sites not yet mined out; victory when it reaches zero.</summary>
    public static IState<int> ResourcesRemaining => instance.resourcesRemaining;

    /// <summary>What the camera can see, and where the cursor points on the board.</summary>
    public static ISignal<View> View => instance.CameraControls.View;

    public static WaveDirector Director => instance.WaveDirector;
    public static Node2D Board => instance.BoardRoot;

    /// <summary>The play area in pixels.</summary>
    public static Rect2 LevelBounds => BoundsOf(instance.Dimensions);

    /// <summary>
    /// The play area a given size would give, in pixels, centred on the origin. Static because
    /// Ground is a [Tool] script and draws the level in the editor, where there's no live Instance.
    /// </summary>
    public static Rect2 BoundsOf(Vector2 metres) {
        var size = metres * World.PixelsPerMetre;
        return new Rect2(-size * 0.5f, size);
    }

    public static CollisionWorld<PowerBody> PowerZone => instance.powerZone;
    public static CollisionWorld<Node2D> GroundZone => instance.groundZone;
    public static CollisionWorld<Radar> RadarZone => instance.radarZone;
    public static CollisionWorld<Turret> TurretZone => instance.turretZone;
    public static CollisionWorld<Repair> RepairZone => instance.repairZone;
    public static CollisionWorld<Enemy> EnemyZone => instance.enemyZone;

    /// <summary>Reloads the scene, restarting from Pregame.</summary>
    public static void Restart() => instance.GetTree().ReloadCurrentScene();

    protected override void Init(EffectBuilder s) {

        // Init runs as this node enters the tree, and _EnterTree propagates top-down, so the hub is
        // published before any unit in the scene reaches for it.
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
