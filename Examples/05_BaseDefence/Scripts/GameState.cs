using UnityEngine;
using UnityEngine.SceneManagement;

namespace Spoke.Examples.BaseDefence {

    public enum GameMode { Pregame, Playing, GameOver, Victory }

    public class GameState : SpokeSingleton<GameState> {

        protected override bool OverrideAutoInstantiate => false;

        [Header("References")]
        [SerializeField] WaveDirector waveDirector;
        [SerializeField] CameraControls cameraControls;
        [SerializeField] Core core;

        [Header("Attributes")]
        [SerializeField] float startMoney;
        [SerializeField] Vector2 dimensions = new Vector2(40f, 40f);

        public static WaveDirector Director => Instance.waveDirector;

        public static ISignal<View> View => Instance.cameraControls.View;

        CollisionWorld<PowerBody> powerZone = new();
        CollisionWorld<GameObject> groundZone = new();
        CollisionWorld<Radar> radarZone = new();
        CollisionWorld<Turret> turretZone = new();
        CollisionWorld<Repair> repairZone = new();
        CollisionWorld<Enemy> enemyZone = new();

        public static CollisionWorld<PowerBody> PowerZone => Instance.powerZone;
        public static CollisionWorld<GameObject> GroundZone => Instance.groundZone;
        public static CollisionWorld<Radar> RadarZone => Instance.radarZone;
        public static CollisionWorld<Turret> TurretZone => Instance.turretZone;
        public static CollisionWorld<Repair> RepairZone => Instance.repairZone;
        public static CollisionWorld<Enemy> EnemyZone => Instance.enemyZone;

        public static Plane GroundPlane => new(Vector3.up, LevelBounds.center);

        // A little height so Contains tolerates points a float-epsilon off the ground plane.
        Bounds levelBounds => new Bounds(transform.position, new Vector3(dimensions.x, 0.1f, dimensions.y));
        public static Bounds LevelBounds => Instance.levelBounds;

        State<GameMode> mode = new();
        public static IState<GameMode> Mode => Instance.mode;

        State<float> money = new();
        public static IState<float> Money => Instance.money;

        // Total money earned per second across all active harvesters.
        State<float> collectRate = new();
        public static IState<float> CollectRate => Instance.collectRate;

        // Resource nodes on the map not yet mined out; victory when it reaches zero.
        State<int> resourcesRemaining = new();
        public static IState<int> ResourcesRemaining => Instance.resourcesRemaining;

        public static void Restart() {
            var activeScene = SceneManager.GetActiveScene();
            UnitySignals.NotifySceneTeardown(activeScene);
            SceneManager.LoadScene(activeScene.buildIndex);
        }

        protected override void Init(EffectBuilder s) {
            var isPlaying = s.Memo(s => s.D(mode) == GameMode.Playing);
            s.Effect(s => Time.timeScale = s.D(isPlaying) ? 1f : 0f);
            s.Phase(isPlaying, s => {
                Money.Set(startMoney);

                // The map is won when every resource on it has been mined out.
                var allMined = s.Memo(s => s.D(resourcesRemaining) == 0);
                s.Phase(allMined, s => mode.Set(GameMode.Victory));

                // And lost when the Core leaves the field — it only despawns when
                // destroyed, after its shatter plays out.
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