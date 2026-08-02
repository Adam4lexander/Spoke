namespace Spoke.Examples.BaseDefence;

/// <summary>
/// Extends the power grid's reach. All the relaying is done by its PowerNode — this class only
/// says what it is.
/// </summary>
public partial class Relay : Building {

    protected override string Blurb =>
        "Extends the power grid, relaying power to any building inside its coverage.\n\n" +
        "Buildings lose power when their path back to the Core is broken.";
}
