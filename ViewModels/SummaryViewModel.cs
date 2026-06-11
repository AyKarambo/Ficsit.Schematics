using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Services;

namespace Ficsit.Schematics.ViewModels;

/// <summary>Aggregates the solve result for the summary panel (Everything / Selected scope).</summary>
public sealed partial class SummaryViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly IconStore _icons;
    private readonly LocalizationService _loc;
    private readonly NumberFormatService _numbers;

    [ObservableProperty]
    public partial string Scope { get; set; } = "Everything";

    public ObservableCollection<string> Scopes { get; } = ["Everything", "Selected"];

    public ObservableCollection<SummaryRow> PowerRows { get; } = [];
    public ObservableCollection<SummaryRow> OverclockRows { get; } = [];
    public ObservableCollection<SummaryRow> OutputRows { get; } = [];
    public ObservableCollection<SummaryRow> InputRows { get; } = [];

    public SummaryViewModel(AppState state, IconStore icons, LocalizationService loc, NumberFormatService numbers)
    {
        _state = state;
        _icons = icons;
        _loc = loc;
        _numbers = numbers;
    }

    partial void OnScopeChanged(string value) => Refresh();

    public void Refresh()
    {
        var result = _state.Editor.Result;
        var nodes = (Scope == "Selected" && _state.Selection.Count > 0
                ? _state.Selection.AsEnumerable()
                : _state.Editor.Document.Root.AllNodes())
            .Where(n => n.Kind is not (NodeKind.Outpost or NodeKind.Blueprint))
            .ToList();
        var nodeSet = nodes.ToHashSet();

        // -------- power
        var used = Rational.Zero;
        var made = Rational.Zero;
        var somersloops = 0;
        var sinkPoints = Rational.Zero;
        foreach (var node in nodes)
        {
            var r = result.For(node);
            if (r.Power.IsNegative) used += -r.Power;
            else made += r.Power;
            somersloops += node.Somersloops;
            sinkPoints += r.SinkPointsPerMinute;
        }

        PowerRows.Clear();
        PowerRows.Add(Row("AVERAGE_NET_POWER", made - used));
        PowerRows.Add(Row("AVERAGE_POWER_USED", used));
        PowerRows.Add(Row("AVERAGE_POWER_MADE", made));

        OverclockRows.Clear();
        OverclockRows.Add(new SummaryRow { Label = _loc.L("Somersloop"), Value = somersloops.ToString() });
        if (sinkPoints.IsPositive)
            OverclockRows.Add(new SummaryRow { Label = _loc.L("SINK_PPM"), Value = _numbers.Summary(sinkPoints) });

        // -------- parts: unused outputs and unmade inputs, aggregated across the scope.
        var unused = new Dictionary<string, Rational>();
        var unmade = new Dictionary<string, Rational>();
        foreach (var node in nodes)
        {
            var r = result.For(node);
            foreach (var (part, port) in r.Outputs)
            {
                // Surplus leaving the scope: production minus what flows to scope members.
                var consumedInScope = _state.Editor.Document.Root.AllConnections()
                    .Where(c => c.From == node && c.Part == part && nodeSet.Contains(c.To))
                    .Aggregate(Rational.Zero, (sum, c) => sum + result.FlowOf(c));
                var surplus = port.Target - consumedInScope;
                if (surplus.IsPositive)
                    unused[part] = unused.GetValueOrDefault(part, Rational.Zero) + surplus;
            }
            foreach (var (part, port) in r.Inputs)
            {
                var deliveredFromScope = _state.Editor.Document.Root.AllConnections()
                    .Where(c => c.To == node && c.Part == part && nodeSet.Contains(c.From))
                    .Aggregate(Rational.Zero, (sum, c) => sum + result.FlowOf(c));
                var missing = port.Target - deliveredFromScope;
                if (missing.IsPositive)
                    unmade[part] = unmade.GetValueOrDefault(part, Rational.Zero) + missing;
            }
        }

        OutputRows.Clear();
        foreach (var (part, ppm) in unused.OrderByDescending(kv => kv.Value))
            OutputRows.Add(new SummaryRow
            {
                Icon = _icons.GetSource(part),
                Label = _loc.L(part),
                Value = _numbers.Summary(ppm),
            });

        InputRows.Clear();
        foreach (var (part, ppm) in unmade.OrderByDescending(kv => kv.Value))
            InputRows.Add(new SummaryRow
            {
                Icon = _icons.GetSource(part),
                Label = _loc.L(part),
                Value = _numbers.Summary(ppm),
            });
    }

    private SummaryRow Row(string key, Rational value)
        => new() { Label = _loc.L(key), Value = _numbers.Summary(value) + " MW" };
}
