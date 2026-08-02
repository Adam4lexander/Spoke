using Godot;

namespace Spoke.Examples.BaseDefence;

// Every colour the game uses, taken from the Unity scene and materials. Unity spreads these
// across serialized fields and material assets; there's no equivalent asset here, so they live
// in one place.
public static class Palette {

    // Ground.
    public static readonly Color Ground = new(1f, 0.6392157f, 0f);
    public static readonly Color SubGround = new(0.39622638f, 0.27824f, 0.07662866f);
    public static readonly Color GroundEdge = new(0.55f, 0.35f, 0.04f);
    public static readonly Color Grid = new(1f, 0.72f, 0.22f);

    // Board overlays.
    public static readonly Color PowerCoverage = new(0f, 1f, 1f);
    public static readonly Color RadarCoverage = new(0.34509802f, 0.36953562f, 1f);
    public static readonly Color TurretCoverage = new(1f, 0.109803915f, 0.15999596f);
    public static readonly Color RepairCoverage = new(0f, 1f, 0.51094913f);
    public static readonly Color PowerLink = new(0f, 1f, 1f);
    public static readonly Color HoverRing = new(1f, 1f, 1f);
    public static readonly Color ValidPlacement = new(0f, 1f, 0f);
    public static readonly Color InvalidPlacement = new(1f, 0f, 0f);

    // Beams and widgets.
    public static readonly Color TurretBeam = new(1f, 0.90588236f, 0.69411767f);
    public static readonly Color RepairBeam = new(0.61960787f, 1f, 0.80784315f);
    public static readonly Color TrackedMarker = new(0.6f, 0.98039216f, 1f);
    public static readonly Color BlastRing = new(1f, 0.35f, 0.15f);

    // Building tint while it has no power.
    public static readonly Color Unpowered = new(0.35f, 0.35f, 0.35f);

    public static readonly Color DamageFlash = new(1f, 0.25f, 0.25f);

    // Health bar.
    public static readonly Color Healthy = new(0.49411765f, 1f, 0.4117647f);
    public static readonly Color Moderate = new(0.9882353f, 1f, 0f);
    public static readonly Color Severe = new(1f, 0.38039216f, 0.38039216f);
    public static readonly Color BarBacking = new(0f, 0f, 0f, 0.55f);

    // Rich-text accents used by the sidebar.
    public static readonly Color Amber = new(1f, 0.7372549f, 0f);
    public static readonly Color Danger = new(1f, 0.3f, 0.3f);
    public static readonly Color PaleBlue = new(0.6f, 0.8f, 1f);

    // Sidebar panel and wave-warning bars.
    public static readonly Color PanelBg = new(0.074092194f, 0.0831238f, 0.14150941f);
    public static readonly Color WarningBar = new(1f, 0.3160377f, 0.3160377f);

    // Text is plain white; the colour comes from rich-text tags.
    public static readonly Color Text = Colors.White;
}
