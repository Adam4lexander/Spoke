using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public enum GameMode { Pregame, Playing, GameOver }

    public class GameState : SpokeSingleton<GameState> {

        [Header("Attributes")]
        [SerializeField] float startMoney;
        [SerializeField] Vector2 dimensions = new Vector2(40f, 40f);

        State<View> view = new();
        public static ISignal<View> View => Instance.view;

        CollisionWorld<PowerBody> powerZone = new();
        CollisionWorld<GameObject> groundZone = new();
        CollisionWorld<Radar> radarZone = new();
        CollisionWorld<Turret> turretZone = new();
        CollisionWorld<Repair> repairZone = new();
        CollisionWorld<Enemy> trackedEnemyZone = new();

        public static CollisionWorld<PowerBody> PowerZone => Instance.powerZone;
        public static CollisionWorld<GameObject> GroundZone => Instance.groundZone;
        public static CollisionWorld<Radar> RadarZone => Instance.radarZone;
        public static CollisionWorld<Turret> TurretZone => Instance.turretZone;
        public static CollisionWorld<Repair> RepairZone => Instance.repairZone;
        public static CollisionWorld<Enemy> TrackedEnemyZone => Instance.trackedEnemyZone;

        public static Plane GroundPlane => new(Vector3.up, LevelBounds.center);

        // A little height so Contains tolerates points a float-epsilon off the ground plane.
        Bounds levelBounds => new Bounds(transform.position, new Vector3(dimensions.x, 0.1f, dimensions.y));
        public static Bounds LevelBounds => Instance.levelBounds;

        public static void RecomputeView(Camera camera) => Instance.view.Set(new View(camera, GroundPlane));

        State<GameMode> mode = new();
        public static IState<GameMode> Mode => Instance.mode;

        State<float> money = new();
        public static IState<float> Money => Instance.money;

        State<int> collectRate = new();
        public static IState<int> CollectRate => Instance.collectRate;

        protected override void Init(EffectBuilder s) {
            var isPlaying = s.Memo(s => s.D(mode) == GameMode.Playing);
            s.Effect(s => Time.timeScale = s.D(isPlaying) ? 1f : 0f);
            s.Phase(isPlaying, s => {
                Money.Set(startMoney);
            });
        }

        void LateUpdate() {
            powerZone.Tick();
            groundZone.Tick();
            radarZone.Tick();
            turretZone.Tick();
            repairZone.Tick();
            trackedEnemyZone.Tick();
        }

        void OnDrawGizmosSelected() {
            var bounds = levelBounds;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}