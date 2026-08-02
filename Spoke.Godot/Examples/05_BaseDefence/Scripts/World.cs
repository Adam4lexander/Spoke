namespace Spoke.Examples.BaseDefence;

/// <summary>
/// The bridge between the original's numbers and this one's.
///
/// The Unity version is a 3D game measured in metres: a turret's range is 5, a relay reaches 3, the
/// level is 40 across. Every one of those values is reproduced exactly here, and every unit scene
/// exports them in those same metres, so a value in a `.tscn` can be read straight against the
/// Unity prefab it came from without arithmetic.
///
/// This is the only place metres become pixels.
/// </summary>
public static class World {

    /// <summary>The one number in the game that isn't from the original.</summary>
    public const float PixelsPerMetre = 64f;

    /// <summary>Metres to pixels.</summary>
    public static float Px(float metres) => metres * PixelsPerMetre;
}
