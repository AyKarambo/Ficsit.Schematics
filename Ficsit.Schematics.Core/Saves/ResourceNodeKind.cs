namespace Ficsit.Schematics.Core.Saves;

public enum ResourceNodeKind
{
    /// <summary>Solid ore / surface crude oil node (takes a Miner or Oil Extractor).</summary>
    Node,

    /// <summary>Geyser (takes a Geothermal Generator).</summary>
    Geyser,

    /// <summary>Resource well head (takes a Resource Well Pressurizer).</summary>
    FrackingCore,

    /// <summary>Resource well satellite (takes a Resource Well Extractor).</summary>
    FrackingSatellite,
}
