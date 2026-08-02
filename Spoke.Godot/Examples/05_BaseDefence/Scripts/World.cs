namespace Spoke.Examples.BaseDefence;

// Unity's version is 3D and measured in metres; this one is 2D and measured in pixels. Every
// gameplay value stays in the original's metres, and this is the only place the two meet.
public static class World {

    public const float PixelsPerMetre = 64f;

    /// <summary>Metres to pixels.</summary>
    public static float Px(float metres) => metres * PixelsPerMetre;
}
