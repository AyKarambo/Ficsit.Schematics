namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// One mark of a multi-mark family: its sort key, <see cref="Mark"/> and whether it is
/// the family's default. The mark's stats (tier, power, throughput, cost) come from the
/// generated <c>MachineStats</c> table via the derived name "{family} Mk.{n}".
/// </summary>
public sealed record MarkSpec(int Sort, Mark Mark, bool IsDefault = false);
