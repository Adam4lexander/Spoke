using UnityEngine;

namespace Spoke {

    public abstract class SpokeBehaviour : MonoBehaviour {

        State<bool> isAwake = State.Create(false);
        State<bool> isEnabled = State.Create(false);
        State<bool> isStarted = State.Create(false);

        public ISignal<bool> IsAwake => isAwake;
        public ISignal<bool> IsEnabled => isEnabled;
        public ISignal<bool> IsStarted => isStarted;

        SpokeHandle sceneTeardown, appTeardown;
        SpokeTree root;

        protected abstract void Init(EffectBuilder s);

        protected virtual void Awake() {
            DoInit();
        }

        protected virtual void OnDestroy() {
            DoTeardown();
        }

        protected virtual void OnEnable() {
            if (root == null) DoInit(); // Domain reload may not run Awake
            isEnabled.Set(true);
        }

        protected virtual void OnDisable() {
            isEnabled.Set(false);
        }

        protected virtual void Start() {
            isStarted.Set(true);
        }

        void DoInit() {
            root = SpokeTree.Spawn($"{GetType().Name}:SpokeTree", new Effect("Init", Init), new UnitySpokeLogger(this));
            sceneTeardown = SpokeTeardown.Scene.Subscribe(scene => { if (scene == gameObject.scene) DoTeardown(); });
            appTeardown = SpokeTeardown.App.Subscribe(() => DoTeardown());
            isAwake.Set(true);
        }

        void DoTeardown() {
            sceneTeardown.Dispose();
            appTeardown.Dispose();
            // Avoid setting enabled=false for ExecuteAlways behaviours on domain reload. Because
            // it will be serialized as disabled on each reload.
            if (Application.isPlaying) enabled = false;
            isEnabled.Set(false);
            root?.Dispose();
            root = default;
            isAwake.Set(false);
        }
    }
}