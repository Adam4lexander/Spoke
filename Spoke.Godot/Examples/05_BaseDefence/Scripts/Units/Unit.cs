using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// Everything on the board that can be hurt: buildings, resource sites, enemies. Owns the shared
/// shape of a unit's life, and reads its body and health bar from its own scene.
///
/// Unity assembles a unit from components — Building, Health, MeshFX, HealthBar, PowerNode all
/// sitting on one GameObject. Godot has no component model, so the same job splits three ways:
/// things with a position or something to draw are child nodes in the .tscn (UnitFX, HealthBar,
/// PowerNode); things that are only state are plain objects (Health); and what was a second
/// component beside Building is a subclass of it (Turret, Radar, Relay, RepairStation, Core).
///
/// The numbers exported here are in metres and hit points, exactly as the Unity prefabs serialize
/// them. See <see cref="World"/>.
/// </summary>
public abstract partial class Unit : SpokeNode2D, IHoverable {

    protected readonly Health health = new();
    protected readonly State<HoverInfo> hoverInfo = State.Create(default(HoverInfo));

    /// <summary>Full health, in hit points.</summary>
    [Export]
    public float MaxHp {
        get => health.MaxHp;
        set => health.MaxHp = value;
    }

    /// <summary>Footprint radius in metres, for hover-picking, placement and blast damage.</summary>
    [Export] public float Radius { get; set; } = 0.4f;

    /// <summary>The same footprint in pixels, which is what the collision worlds work in.</summary>
    public float RadiusPx => World.Px(Radius);

    protected UnitFX FX { get; private set; }
    protected HealthBar Bar { get; private set; }

    public Health Health => health;
    public ISignal<HoverInfo> HoverInfo => hoverInfo;

    /// <summary>False for units that fly — an enemy doesn't block a building's placement.</summary>
    protected virtual bool OccupiesGround => true;

    /// <summary>False where the bar means something other than health, as on a resource site.</summary>
    protected virtual bool BarTracksHealth => true;

    /// <summary>
    /// Sealed, because the shape of a unit's life is the same for all of them. The three hooks
    /// below are where a subclass joins in, and each one names exactly when it runs.
    /// </summary>
    protected sealed override void Init(EffectBuilder s) {
        FX = GetNode<UnitFX>("FX");
        Bar = GetNode<HealthBar>("HealthBar");

        if (BarTracksHealth) {
            s.Effect(s => {
                var frac = s.D(health.HPFraction);
                Bar.Visible = s.D(health.IsAlive) && frac < 1f;
                Bar.Fraction.Set(frac);
            });
        }

        Always(s);

        // IsReady as well as IsInTree, because a unit's body is its own children: UnitFX doesn't
        // know its pieces — or which one is the Pivot a turret aims — until its own Init has run,
        // and that happens after this one. Waiting a beat is a phase, not a callback.
        var isLive = s.Memo(s => s.D(IsReady) && s.D(IsInTree));

        s.Phase(isLive, s => {

            // Health is mounted here rather than above so that its cleanup — which clears the
            // damage taken — runs when the unit leaves the board. That's what makes a pooled unit
            // come back at full health without anyone writing a reset path.
            s.Effect("Health", health.Mount);

            s.Phase(health.IsAlive, s => {
                if (OccupiesGround) {
                    s.Use(GameState.GroundZone.AddCollider(this, () => new Circle(GlobalPosition, RadiusPx)));
                }
                s.Subscribe(health.Damaged, () => FX.Blink(Palette.DamageFlash));
                Alive(s);
            });

            var isDead = s.Memo(s => !s.D(health.IsAlive));
            s.Phase(isDead, s => {
                FX.Shatter();
                s.OnCleanup(FX.Restore);
                Dying(s);

                // The unit holds its place on the board until the shatter finishes, then goes back
                // to the pool. Despawning drops it out of the tree, which unmounts this whole
                // IsInTree phase — including the effect doing the despawning.
                s.Effect(s => {
                    if (s.D(FX.IsShattered)) Pool.Despawn(this);
                });
            });
        });
    }

    /// <summary>Runs for the unit's whole existence, in the pool and out. Use for hover text and the like.</summary>
    protected virtual void Always(EffectBuilder s) { }

    /// <summary>Runs while the unit is on the board and alive. Where nearly everything belongs.</summary>
    protected virtual void Alive(EffectBuilder s) { }

    /// <summary>Runs from the moment the unit dies until it leaves the board.</summary>
    protected virtual void Dying(EffectBuilder s) { }
}
