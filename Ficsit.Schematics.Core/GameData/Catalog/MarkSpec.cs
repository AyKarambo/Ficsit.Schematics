namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>A machine mark within a multi-mark family: the machine it builds plus its variant entry.</summary>
public sealed record MarkSpec(int Sort, MachineDefinition Machine, MultiMachineVariant Variant);
