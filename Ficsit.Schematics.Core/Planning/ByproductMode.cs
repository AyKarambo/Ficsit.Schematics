namespace Ficsit.Schematics.Core.Planning;

/// <summary>How the auto-planner deals with byproducts.</summary>
public enum ByproductMode
{
    /// <summary>
    /// Recycle byproducts back into the chain wherever possible (zero waste);
    /// sinking is a heavily penalized last resort.
    /// </summary>
    Eliminate,

    /// <summary>Byproducts may go straight into the AWESOME Sink.</summary>
    AllowSink,
}
