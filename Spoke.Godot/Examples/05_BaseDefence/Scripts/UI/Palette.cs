using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// Every colour the game uses. The overlay and beam colours are the ones serialized in the Unity
/// scene and materials; the ground and UI colours come from its materials too.
/// </summary>
public static class Palette {

    // Ground and SubGround materials.
    public static readonly Color Ground = new(1f, 0.6392157f, 0f);
    public static readonly Color SubGround = new(0.39622638f, 0.27824f, 0.07662866f);
    public static readonly Color GroundEdge = new(0.55f, 0.35f, 0.04f);
    public static readonly Color Grid = new(1f, 0.72f, 0.22f);

    // BoardInteractions, serialized in BaseDefence.unity.
    public static readonly Color PowerCoverage = new(0f, 1f, 1f);
    public static readonly Color RadarCoverage = new(0.34509802f, 0.36953562f, 1f);
    public static readonly Color TurretCoverage = new(1f, 0.109803915f, 0.15999596f);
    public static readonly Color RepairCoverage = new(0f, 1f, 0.51094913f);
    public static readonly Color PowerLink = new(0f, 1f, 1f);
    public static readonly Color HoverRing = new(1f, 1f, 1f);
    public static readonly Color ValidPlacement = new(0f, 1f, 0f);
    public static readonly Color InvalidPlacement = new(1f, 0f, 0f);

    // Beam and widget materials.
    public static readonly Color TurretBeam = new(1f, 0.90588236f, 0.69411767f);
    public static readonly Color RepairBeam = new(0.61960787f, 1f, 0.80784315f);
    public static readonly Color TrackedMarker = new(0.6f, 0.98039216f, 1f);
    public static readonly Color BlastRing = new(1f, 0.35f, 0.15f);

    /// <summary>Building tint while it has no power. Unity's Building.unpoweredDim, 0.35.</summary>
    public static readonly Color Unpowered = new(0.35f, 0.35f, 0.35f);

    public static readonly Color DamageFlash = new(1f, 0.25f, 0.25f);

    // Health bar, serialized on the HealthBar prefab.
    public static readonly Color Healthy = new(0.49411765f, 1f, 0.4117647f);
    public static readonly Color Moderate = new(0.9882353f, 1f, 0f);
    public static readonly Color Severe = new(1f, 0.38039216f, 0.38039216f);
    public static readonly Color BarBacking = new(0f, 0f, 0f, 0.55f);

    // The rich-text accents SideBar.cs declares in the Unity version.
    public static readonly Color Amber = new(1f, 0.7372549f, 0f);
    public static readonly Color Danger = new(1f, 0.3f, 0.3f);
    public static readonly Color PaleBlue = new(0.6f, 0.8f, 1f);

    /// <summary>The sidebar panel Image, and the wave-warning bars, from BaseDefence.unity.</summary>
    public static readonly Color PanelBg = new(0.074092194f, 0.0831238f, 0.14150941f);
    public static readonly Color WarningBar = new(1f, 0.3160377f, 0.3160377f);

    /// <summary>Every Text in the Unity scene is plain white; the colour comes from rich-text tags.</summary>
    public static readonly Color Text = Colors.White;
}
