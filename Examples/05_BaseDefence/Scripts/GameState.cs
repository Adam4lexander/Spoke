using UnityEngine;
using UnityEngine.SceneManagement;

namespace Spoke.Examples.BaseDefence {

    /// <summary>The states a playthrough moves through. Only Playing runs the simulation; the rest freeze it.</summary>
    public enum GameMode { Pregame, Playing, GameOver, Victory }

    // The game's central hub: a hand-placed singleton holding the shared state every system reads.
    // It also owns the win/loss conditions (see Init).
    public class GameState : SpokeSingleton<GameState> {

        // Intended to be hand-placed in scene, so disable auto-instantiate (so it throws an error if it doesn't exist)
        protected override bool OverrideAutoInstantiate => false;

        [Header("References")]
        [SerializeField] WaveDirector waveDirector;
        [SerializeField] CameraControls cameraControls;
        [SerializeField] Core core;

        [Header("Attributes")]
        [SerializeField] float startMoney;
        [SerializeField] Vector2 dimensions = new Vector2(40f, 40f);

        State<GameMode> mode = new();
        State<float> money = new();
        State<float> collectRate = new();
        State<int> resourcesRemaining = new();

        // One spatial world per query concern. Units register colliders/sensors and read overlaps;
        // all are ticked once per frame in LateUpdate.
        CollisionWorld<PowerBody> powerZone = new();
        CollisionWorld<GameObject> groundZone = new();
        CollisionWorld<Radar> radarZone = new();
        CollisionWorld<Turret> turretZone = new();
        CollisionWorld<Repair> repairZone = new();
        CollisionWorld<Enemy> enemyZone = new();

        // A little height so Contains tolerates points a float-epsilon off the ground plane.
        Bounds levelBounds => new Bounds(transform.position, new Vector3(dimensions.x, 0.1f, dimensions.y));

        public static WaveDirector Director => Instance.waveDirector;

        // The current camera view: what ground the camera sees, and where the cursor points on it.
        public static ISignal<View> View => Instance.cameraControls.View;

        public static IState<GameMode> Mode => Instance.mode;
        public static IState<float> Money => Instance.money;

        // Total money earned per second across all active harvesters.
        public static IState<float> CollectRate => Instance.collectRate;

        // Resource nodes on the map not yet mined out; victory when it reaches zero.
        public static IState<int> ResourcesRemaining => Instance.resourcesRemaining;

        public static CollisionWorld<PowerBody> PowerZone => Instance.powerZone;
        public static CollisionWorld<GameObject> GroundZone => Instance.groundZone;
        public static CollisionWorld<Radar> RadarZone => Instance.radarZone;
        public static CollisionWorld<Turret> TurretZone => Instance.turretZone;
        public static CollisionWorld<Repair> RepairZone => Instance.repairZone;
        public static CollisionWorld<Enemy> EnemyZone => Instance.enemyZone;

        public static Plane GroundPlane => new(Vector3.up, LevelBounds.center);
        public static Bounds LevelBounds => Instance.levelBounds;

        /// <summary>Reloads the active scene, restarting the game from Pregame.</summary>
        public static void Restart() {
            var activeScene = SceneManager.GetActiveScene();
            // Let Spoke tear its trees down cleanly before the scene reloads.
            UnitySignals.NotifySceneTeardown(activeScene);
            SceneManager.LoadScene(activeScene.buildIndex);
        }

        protected override void Init(EffectBuilder s) {
            var isPlaying = s.Memo(s => s.D(mode) == GameMode.Playing);
            
            // Pause the sim unless we're in GameMode.Playing
            s.Effect(s => Time.timeScale = s.D(isPlaying) ? 1f : 0f);
            
            s.Phase(isPlaying, s => {
                Money.Set(startMoney);

                // Victory once every resource on the map has been mined out.
                var allMined = s.Memo(s => s.D(resourcesRemaining) == 0);
                s.Phase(allMined, s => mode.Set(GameMode.Victory));

                // Game Over the Core is gone. It leaves the field only after its death shatter finishes.
                var coreLost = s.Memo(s => !s.D(core.IsEnabled));
                s.Phase(coreLost, s => mode.Set(GameMode.GameOver));
            });
        }

        void LateUpdate() {
            powerZone.Tick();
            groundZone.Tick();
            radarZone.Tick();
            turretZone.Tick();
            repairZone.Tick();
            enemyZone.Tick();
        }

        void OnDrawGizmosSelected() {
            var bounds = levelBounds;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}