namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// One mark of a multi-mark family in authoring form: the <see cref="Mark"/> and its
/// per-mark stats. The family stamps the final name ("{family} Mk.{n}") onto both the
/// machine and the variant.
/// </summary>
public sealed record MarkSpec(
    int Sort,
    Mark Mark,
    Tier Tier,
    string Throughput,
    bool IsDefault = false,
    string? Power = null,
    CostEntry[]? Cost = null);
