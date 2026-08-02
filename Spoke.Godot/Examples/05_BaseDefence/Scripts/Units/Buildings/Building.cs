using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

// The common component on every building. Adds the behaviour every building shares
// (health bar, footprint collider, damage flash, death shatter).
public partial class Building : SpokeNode {

    static readonly List<Building> all = new();
    /// <summary>Every building currently alive on the map.</summary>
    public static ReadOnlyList<Building> All => new(all);

    [ExportGroup("References")]
    [Export] public Health Health { get; set; }
    [Export] public HealthBar HealthBar { get; set; }
    [Export] public UnitFX FX { get; set; }
    [Export] public PowerNode Power { get; set; }

    [ExportGroup("Attributes")]
    [Export] public string DisplayName { get; set; } = "";
    [Export] public int Cost { get; set; }
    [Export] public float Radius { get; set; } = 0.6f;
    [Export] float unpoweredDim { get => _unpoweredDim.Now; set => _unpoweredDim.Set(value); }

    readonly State<float> _unpoweredDim = State.Create(0.35f);

    [Export] public Unit Unit { get; set; }

    protected override void Init(EffectBuilder s) {
        s.Effect(s => {
            var showHealth = s.D(Health.IsAlive) && s.D(Health.HPFraction) < 1f;
            HealthBar.Visible = showHealth;
            HealthBar.Fraction.Set(s.D(Health.HPFraction));
        });

        s.Phase(IsInTree, s => {
            s.Phase(Health.IsAlive, s => {
                all.Add(this);
                s.OnCleanup(() => all.Remove(this));

                // Physical footprint for hover-picking and blast damage (distinct from the network
                // receiver the PowerNode registers in the power world).
                s.Use(GameState.GroundZone.AddCollider(Unit, () => new Circle(Unit.GlobalPosition, World.Px(Radius))));

                s.Subscribe(Health.Damaged, () => FX.Blink(Palette.DamageFlash));
            });

            var isDead = s.Memo(s => !s.D(Health.IsAlive));
            s.Phase(isDead, s => {
                FX.Shatter();
                Power.Enabled.Set(false);
                s.OnCleanup(() => {
                    FX.Restore();
                    Power.Enabled.Set(true);
                });
                s.Effect(s => {
                    if (s.D(FX.IsShattered)) Pool.Despawn(Unit);
                });
            });
        });

        s.Effect(s => {
            if (s.D(Power.HasPower)) {
                FX.SetTint(Colors.White);
                return;
            }
            var d = s.D(_unpoweredDim);
            FX.SetTint(new Color(d, d, d, 1f));
        });
    }
}
