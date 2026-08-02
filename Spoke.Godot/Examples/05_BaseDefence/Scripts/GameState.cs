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

    /// <summary>Starting cash. Unity's GameState.startMoney.</summary>
    [Export] public float StartMoney { get; set; } = 100f;

    /// <summary>Play area in metres. Unity's GameState.dimensions.</summary>
    [Export] public Vector2 Dimensions { get; set; } = new(40f, 40f);

    // Godot's answer to Unity's prefab fields: the scene picker in the Inspector, stored in the
    // .tscn as a uid reference. These are the kinds that get spawned at runtime — the ones placed
    // at the start are instanced in the scene instead, and need no reference here.
    [ExportGroup("Units")]
    [Export] public PackedScene CoreScene { get; set; }
    [Export] public PackedScene RelayScene { get; set; }
    [Export] public PackedScene RadarScene { get; set; }
    [Export] public PackedScene TurretScene { get; set; }
    [Export] public PackedScene RepairScene { get; set; }
    [Export] public PackedScene ResourceSiteScene { get; set; }
    [Export] public PackedScene Enemy1Scene { get; set; }
    [Export] public PackedScene Enemy2Scene { get; set; }
    [Export] public PackedScene Enemy3Scene { get; set; }
    [Export] public PackedScene BombBlastScene { get; set; }

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
    readonly CollisionWorld<Unit> groundZone = new();
    readonly CollisionWorld<Radar> radarZone = new();
    readonly CollisionWorld<Turret> turretZone = new();
    readonly CollisionWorld<RepairStation> repairZone = new();
    readonly CollisionWorld<Enemy> enemyZone = new();

    Node2D board;
    CameraControls camera;
    WaveDirector director;
    BoardInteractions interactions;

    public static IState<GameMode> Mode => instance.mode;
    public static IState<float> Money => instance.money;

    /// <summary>Total money earned per second across all active harvesters.</summary>
    public static IState<float> CollectRate => instance.collectRate;

    /// <summary>Resource sites not yet mined out; victory when it reaches zero.</summary>
    public static IState<int> ResourcesRemaining => instance.resourcesRemaining;

    /// <summary>What the camera can see, and where the cursor points on the board.</summary>
    public static ISignal<View> View => instance.camera.View;

    public static WaveDirector Director => instance.director;
    public static BoardInteractions Interactions => instance.interactions;

    /// <summary>Everything in the world lives under here. Units are spawned into it by the Pool.</summary>
    public static Node2D Board => instance.board;

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
    public static CollisionWorld<Unit> GroundZone => instance.groundZone;
    public static CollisionWorld<Radar> RadarZone => instance.radarZone;
    public static CollisionWorld<Turret> TurretZone => instance.turretZone;
    public static CollisionWorld<RepairStation> RepairZone => instance.repairZone;
    public static CollisionWorld<Enemy> EnemyZone => instance.enemyZone;

    /// <summary>Reloads the scene, restarting from Pregame.</summary>
    public static void Restart() => instance.GetTree().ReloadCurrentScene();

    protected override void Init(EffectBuilder s) {

        // All of this has to be in place before any descendant's Init, because the units placed in
        // the scene reach for it from theirs. It is, and without a single line of setup outside
        // this method: Init runs as the node enters the tree, and that propagates top-down.
        instance = this;
        Units.Bind(this);

        board = GetNode<Node2D>("Board");
        camera = GetNode<CameraControls>("Camera");
        director = GetNode<WaveDirector>("WaveDirector");
        interactions = GetNode<BoardInteractions>("Board/Interactions");

        // Colliders are sampled from node positions, so the worlds are refreshed once a frame
        // before anything reads them. Being first is the whole requirement, and it comes free from
        // the same ordering: this connects to ProcessFrame before any unit's s.OnProcess does.
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

            // Defeat once the Core has left the board. This phase mounts on the first Start, long
            // after every unit in the scene has initialised, so the Core is already standing.
            var coreLost = s.Memo(s => !s.D(Core.IsStanding));
            s.Phase(coreLost, s => mode.Set(GameMode.GameOver));
        });
    }
}
