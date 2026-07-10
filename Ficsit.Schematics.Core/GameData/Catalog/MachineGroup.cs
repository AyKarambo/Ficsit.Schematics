namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// One machine, or a family of machines, plus the optional multi-machine family that
/// describes its marks/capacities. <see cref="MachineModule"/>'s builders produce these.
/// </summary>
public sealed record MachineGroup(
    IReadOnlyList<(int Sort, MachineDefinition Definition)> Machines,
    int FamilySort = -1,
    MultiMachineDefinition? Family = null)
{
    /// <summary>Attach a capacity-only family (purity / belt mark / upload rate + node
    /// defaults) to a single standalone machine; the family is keyed by the machine's name.</summary>
    public MachineGroup WithFamily(int familySort, bool showPpm = false, bool autoRound = true,
        string defaultMax = "", MultiMachineCapacity[]? capacities = null)
        => this with
        {
            FamilySort = familySort,
            Family = new MultiMachineDefinition
            {
                Name = Machines[0].Definition.Name,
                ShowPpm = showPpm,
                AutoRound = autoRound,
                DefaultMax = defaultMax,
                Capacities = (capacities ?? []).ToList(),
            },
        };
}
