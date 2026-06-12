using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Solver;

/// <summary>
/// The "Basic" calculator: entered values are limits; flows propagate through the
/// graph by iterative bound relaxation. Splitter/merger priorities are ignored
/// (surplus splits proportionally to demand). Unconnected recipe inputs do not
/// constrain a machine — they surface as "unmade" amounts instead, matching the
/// reference behavior.
/// </summary>
public sealed class BasicSolver(GameDatabase data) : ISolver
{
    public string Name => "Basic";

    /// <summary>Demand-driven pull: sources without limits follow their consumers.</summary>
    public bool EnableDemandPull { get; init; } = true;

    /// <summary>
    /// Manual mode: entered values are desired outcomes — a machine with a limit is
    /// pinned to it even when supply falls short (mismatch is flagged instead).
    /// </summary>
    public bool LimitsAreExact { get; init; }

    private sealed class State
    {
        public required NodeProfile Profile;

        /// <summary>Solved machine count; null = still unbounded.</summary>
        public Rational? Count;

        /// <summary>Pass-through nodes track throughput ppm instead of a count.</summary>
        public bool IsThroughput;
    }

    public SolveResult Solve(FactoryDocument document)
    {
        var nodes = document.Root.AllNodes()
            .Where(n => n.Kind is not (NodeKind.Outpost or NodeKind.Blueprint))
            .ToList();
        var connections = document.Root.AllConnections().ToList();

        var states = nodes.ToDictionary(
            n => n,
            n => new State
            {
                Profile = NodeProfile.Build(n, data, document),
            });
        foreach (var state in states.Values)
            state.IsThroughput = state.Profile.IsPassThrough
                || state.Profile.IsOpenSink
                || state.Profile.IsOpenSource;

        var incoming = connections.GroupBy(c => c.To).ToDictionary(g => g.Key, g => g.ToList());
        var outgoing = connections.GroupBy(c => c.From).ToDictionary(g => g.Key, g => g.ToList());
        var flows = connections.ToDictionary(c => c, _ => (Rational?)null);

        var maxPasses = Math.Clamp(nodes.Count * 2 + 6, 10, 120);
        for (var pass = 0; pass < maxPasses; pass++)
        {
            var changed = false;

            foreach (var node in nodes)
            {
                var state = states[node];
                var newCount = ComputeCount(node, state, states, incoming, outgoing, flows);
                if (!NullableEquals(newCount, state.Count))
                {
                    state.Count = newCount;
                    changed = true;
                }
            }

            // Re-allocate flows from current counts: each producer offers, each
            // consumer requests; a connection carries min(offer share, request share).
            foreach (var connection in connections)
            {
                var newFlow = AllocateFlow(connection, states, incoming, outgoing, flows);
                if (!NullableEquals(newFlow, flows[connection]))
                {
                    flows[connection] = newFlow;
                    changed = true;
                }
            }

            if (!changed) break;
        }

        return BuildResult(nodes, connections, states, incoming, outgoing, flows);
    }

    // ------------------------------------------------------------------ counts

    private Rational? ComputeCount(
        FactoryNode node,
        State state,
        Dictionary<FactoryNode, State> states,
        Dictionary<FactoryNode, List<NodeConnection>> incoming,
        Dictionary<FactoryNode, List<NodeConnection>> outgoing,
        Dictionary<NodeConnection, Rational?> flows)
    {
        var profile = state.Profile;
        var inConns = incoming.GetValueOrDefault(node) ?? [];
        var outConns = outgoing.GetValueOrDefault(node) ?? [];

        if (state.IsThroughput)
        {
            // Pass-through / storage / sink: value is ppm moved through the node.
            Rational? limitPpm = profile.LimitCount is { } lc ? lc * profile.PpmUnit : null;

            Rational? supply = profile.IsOpenSource ? null : SumKnown(inConns, flows);
            Rational? demand = profile.IsOpenSink
                ? null
                : profile.IsPassThrough
                    ? SumConsumerRequests(node, outConns, states, incoming, flows)
                    : Rational.Zero;

            var value = MinN(MinN(supply, demand), limitPpm);
            if (profile.IsOpenSink && !profile.IsPassThrough)
                value = MinN(SumKnown(inConns, flows), limitPpm); // sinks absorb what arrives
            return value;
        }

        // Recipe machines.
        if (LimitsAreExact && profile.LimitCount is { } exact)
            return exact;

        var bounds = new List<Rational>();
        if (profile.LimitCount is { } limit) bounds.Add(limit);

        foreach (var partGroup in inConns.GroupBy(c => c.Part))
        {
            if (!profile.InRates.TryGetValue(partGroup.Key, out var needRate) || !needRate.IsPositive)
                continue;
            var delivered = SumKnown(partGroup, flows);
            if (delivered is null) continue; // unbounded upstream, does not constrain
            bounds.Add(delivered.Value / needRate);
        }

        if (bounds.Count > 0)
            return bounds.Min();

        if (EnableDemandPull && outConns.Count > 0 && profile.OutRates.Count > 0)
        {
            // No limit and no constraining input: follow consumer demand.
            Rational? maxNeeded = Rational.Zero;
            foreach (var partGroup in outConns.GroupBy(c => c.Part))
            {
                if (!profile.OutRates.TryGetValue(partGroup.Key, out var makeRate) || !makeRate.IsPositive)
                    continue;
                var requested = SumConsumerRequestsFor(node, partGroup, states, incoming, flows);
                if (requested is null) { maxNeeded = null; break; }
                maxNeeded = Rational.Max(maxNeeded!.Value, requested.Value / makeRate);
            }
            return maxNeeded;
        }

        return null;
    }

    // ------------------------------------------------------------------- flows

    private Rational? AllocateFlow(
        NodeConnection connection,
        Dictionary<FactoryNode, State> states,
        Dictionary<FactoryNode, List<NodeConnection>> incoming,
        Dictionary<FactoryNode, List<NodeConnection>> outgoing,
        Dictionary<NodeConnection, Rational?> flows)
    {
        var producer = states[connection.From];
        var consumer = states[connection.To];

        // What the producer can offer on this connection.
        var offerTotal = ProducerOffer(producer, connection.Part, incoming, flows);
        var siblingsOut = (outgoing.GetValueOrDefault(connection.From) ?? [])
            .Where(c => c.Part == connection.Part).ToList();
        var requestHere = ConsumerRequest(consumer, connection.Part, incoming, flows);
        Rational? requestAll = Rational.Zero;
        foreach (var sibling in siblingsOut)
        {
            var r = ConsumerRequest(states[sibling.To], sibling.Part, incoming, flows);
            requestAll = AddN(requestAll, DistributeRequest(sibling, r, incoming, states, flows));
        }
        var myRequest = DistributeRequest(connection, requestHere, incoming, states, flows);

        Rational? granted;
        if (offerTotal is null)
            granted = myRequest;                       // unlimited producer satisfies the request
        else if (requestAll is null || myRequest is null)
            granted = offerTotal;                      // unlimited demand absorbs the full offer (split below)
        else if (requestAll.Value.IsZero)
            granted = Rational.Zero;
        else if (offerTotal.Value >= requestAll.Value)
            granted = myRequest;
        else
            granted = offerTotal.Value * myRequest.Value / requestAll.Value; // proportional share

        if (granted is null) return null;

        // Unlimited-demand consumers split the producer's offer evenly across connections.
        if ((requestAll is null || myRequest is null) && offerTotal is not null)
        {
            var unlimitedCount = siblingsOut.Count(c =>
                ConsumerRequest(states[c.To], c.Part, incoming, flows) is null);
            if (unlimitedCount > 0)
            {
                var boundedTotal = Rational.Zero;
                foreach (var sibling in siblingsOut)
                {
                    var r = DistributeRequest(sibling, ConsumerRequest(states[sibling.To], sibling.Part, incoming, flows), incoming, states, flows);
                    if (r is not null) boundedTotal += r.Value;
                }
                var leftover = Rational.Max(offerTotal.Value - boundedTotal, Rational.Zero);
                if (myRequest is null)
                    return leftover / unlimitedCount;
            }
        }

        return granted;
    }

    /// <summary>ppm of a part the producer can put on its outgoing connections in total.</summary>
    private Rational? ProducerOffer(
        State producer, string part,
        Dictionary<FactoryNode, List<NodeConnection>> incoming,
        Dictionary<NodeConnection, Rational?> flows)
    {
        var profile = producer.Profile;
        if (profile.IsOpenSource)
            return profile.LimitCount is { } lc ? lc * profile.PpmUnit : null;
        if (profile.IsPassThrough)
            return producer.Count; // throughput ppm
        if (profile.OutRates.TryGetValue(part, out var rate) && rate.IsPositive)
            return producer.Count is { } count ? count * rate : null;
        return Rational.Zero;
    }

    /// <summary>ppm of a part the consumer wants in total (across all its suppliers of that part).</summary>
    private Rational? ConsumerRequest(
        State consumer, string part,
        Dictionary<FactoryNode, List<NodeConnection>> incoming,
        Dictionary<NodeConnection, Rational?> flows)
    {
        var profile = consumer.Profile;
        if (profile.IsOpenSink || profile.IsPassThrough)
        {
            Rational? limitPpm = profile.LimitCount is { } lc ? lc * profile.PpmUnit : null;
            if (profile.IsPassThrough)
                return MinN(limitPpm, consumer.Count is { } t ? t : null) ?? limitPpm;
            return limitPpm;
        }
        if (profile.InRates.TryGetValue(part, out var rate) && rate.IsPositive)
        {
            if (consumer.Count is { } count) return count * rate;
            if (profile.LimitCount is { } limit) return limit * rate;
            return null;
        }
        return Rational.Zero;
    }

    /// <summary>Splits a consumer's total request for a part across its incoming connections.</summary>
    private Rational? DistributeRequest(
        NodeConnection connection, Rational? totalRequest,
        Dictionary<FactoryNode, List<NodeConnection>> incoming,
        Dictionary<FactoryNode, State> states,
        Dictionary<NodeConnection, Rational?> flows)
    {
        if (totalRequest is null) return null;
        var suppliers = (incoming.GetValueOrDefault(connection.To) ?? [])
            .Where(c => c.Part == connection.Part).ToList();
        if (suppliers.Count <= 1) return totalRequest;

        // Allocate to bounded offers proportionally; unlimited offers share the rest evenly.
        var offers = suppliers.ToDictionary(
            c => c,
            c => ProducerOffer(states[c.From], c.Part, incoming, flows));
        var myOffer = offers[connection];
        Rational boundedSum = Rational.Zero;
        var unlimited = 0;
        foreach (var offer in offers.Values)
        {
            if (offer is null) unlimited++;
            else boundedSum += offer.Value;
        }

        if (myOffer is not null)
        {
            if (boundedSum >= totalRequest.Value && boundedSum.IsPositive)
                return totalRequest.Value * myOffer.Value / boundedSum;
            return myOffer.Value;
        }

        var remaining = Rational.Max(totalRequest.Value - boundedSum, Rational.Zero);
        return unlimited > 0 ? remaining / unlimited : Rational.Zero;
    }

    // ------------------------------------------------------------------ result

    private SolveResult BuildResult(
        List<FactoryNode> nodes,
        List<NodeConnection> connections,
        Dictionary<FactoryNode, State> states,
        Dictionary<FactoryNode, List<NodeConnection>> incoming,
        Dictionary<FactoryNode, List<NodeConnection>> outgoing,
        Dictionary<NodeConnection, Rational?> flows)
    {
        var result = new SolveResult();

        foreach (var connection in connections)
            result.Flows[connection] = flows[connection] ?? Rational.Zero;

        foreach (var node in nodes)
        {
            var state = states[node];
            var profile = state.Profile;
            var nodeResult = new NodeResult();
            var count = state.Count ?? Rational.Zero;

            if (state.IsThroughput)
            {
                var throughput = count; // ppm
                nodeResult.Count = profile.PpmUnit.IsPositive ? throughput / profile.PpmUnit : throughput;
                nodeResult.DisplayValue = throughput;
                nodeResult.IsPpmDisplay = true;
                nodeResult.Power = nodeResult.Count * profile.PowerPerMachine;

                var inFlow = SumKnown(incoming.GetValueOrDefault(node) ?? [], flows) ?? Rational.Zero;
                var outFlow = SumKnown(outgoing.GetValueOrDefault(node) ?? [], flows) ?? Rational.Zero;
                foreach (var group in (incoming.GetValueOrDefault(node) ?? []).GroupBy(c => c.Part))
                    nodeResult.Inputs[group.Key] = new PortResult
                    {
                        Target = SumKnown(group, flows) ?? Rational.Zero,
                        Connected = SumKnown(group, flows) ?? Rational.Zero,
                        HasConnections = true,
                    };
                foreach (var group in (outgoing.GetValueOrDefault(node) ?? []).GroupBy(c => c.Part))
                    nodeResult.Outputs[group.Key] = new PortResult
                    {
                        Target = SumKnown(group, flows) ?? Rational.Zero,
                        Connected = SumKnown(group, flows) ?? Rational.Zero,
                        HasConnections = true,
                    };

                if (profile.IsAwesomeSink)
                {
                    var points = Rational.Zero;
                    foreach (var group in (incoming.GetValueOrDefault(node) ?? []).GroupBy(c => c.Part))
                    {
                        var ppm = SumKnown(group, flows) ?? Rational.Zero;
                        if (data.PartsByName.TryGetValue(group.Key, out var part))
                            points += ppm * part.SinkPoints;
                    }
                    nodeResult.SinkPointsPerMinute = points;
                }

                if (profile.IsPassThrough && node.StorageMode == StorageMode.InputEqualsOutput
                    && node.Kind == NodeKind.StorageContainer && inFlow != outFlow)
                    nodeResult.IsInvalid = true;
            }
            else
            {
                // `count` stays the exact fractional count: port targets and flows
                // below must be untouched by Auto-Round (hard invariant).
                nodeResult.Count = count;
                nodeResult.IsPpmDisplay = profile.IsPpmDisplay;
                nodeResult.DisplayValue = profile.IsPpmDisplay ? count * profile.PpmUnit : count;
                nodeResult.Power = count * profile.PowerPerMachine;

                // Auto-Round: present the same throughput as a whole machine count
                // with the per-machine clock rebalanced down (count was solved at the
                // entered clock, so N = ceil(count) and effClock = count·clock/N).
                if (node.AutoRound && node.Kind == NodeKind.Recipe && count.IsPositive)
                {
                    var whole = new Rational(count.Ceiling());
                    var effectiveClock = count * node.ClockSpeed / whole;
                    if (effectiveClock > FactoryNode.MinClockSpeed
                        && effectiveClock <= FactoryNode.MaxClockSpeed)
                    {
                        nodeResult.Count = whole;
                        nodeResult.IsRounded = true;
                        nodeResult.EffectiveClock = effectiveClock;
                        if (!profile.IsPpmDisplay)
                            nodeResult.DisplayValue = whole;
                        nodeResult.Power = whole * profile.PowerPerMachineAt(effectiveClock);
                    }
                }

                foreach (var (part, rate) in profile.InRates)
                {
                    var conns = (incoming.GetValueOrDefault(node) ?? []).Where(c => c.Part == part).ToList();
                    nodeResult.Inputs[part] = new PortResult
                    {
                        Target = count * rate,
                        Connected = SumKnown(conns, flows) ?? Rational.Zero,
                        HasConnections = conns.Count > 0,
                    };
                }
                foreach (var (part, rate) in profile.OutRates)
                {
                    var conns = (outgoing.GetValueOrDefault(node) ?? []).Where(c => c.Part == part).ToList();
                    nodeResult.Outputs[part] = new PortResult
                    {
                        Target = count * rate,
                        Connected = SumKnown(conns, flows) ?? Rational.Zero,
                        HasConnections = conns.Count > 0,
                    };
                }
            }

            result.Nodes[node.Id] = nodeResult;
        }

        return result;
    }

    // ----------------------------------------------------------------- helpers

    private static Rational? SumKnown(IEnumerable<NodeConnection> connections, Dictionary<NodeConnection, Rational?> flows)
    {
        var sum = Rational.Zero;
        foreach (var connection in connections)
        {
            var flow = flows.GetValueOrDefault(connection);
            if (flow is null) return null;
            sum += flow.Value;
        }
        return sum;
    }

    private Rational? SumConsumerRequests(
        FactoryNode node,
        List<NodeConnection> outConns,
        Dictionary<FactoryNode, State> states,
        Dictionary<FactoryNode, List<NodeConnection>> incoming,
        Dictionary<NodeConnection, Rational?> flows)
    {
        Rational? total = Rational.Zero;
        foreach (var connection in outConns)
        {
            var request = DistributeRequest(connection,
                ConsumerRequest(states[connection.To], connection.Part, incoming, flows),
                incoming, states, flows);
            total = AddN(total, request);
            if (total is null) return null;
        }
        return total;
    }

    private Rational? SumConsumerRequestsFor(
        FactoryNode node,
        IEnumerable<NodeConnection> partConns,
        Dictionary<FactoryNode, State> states,
        Dictionary<FactoryNode, List<NodeConnection>> incoming,
        Dictionary<NodeConnection, Rational?> flows)
    {
        Rational? total = Rational.Zero;
        foreach (var connection in partConns)
        {
            var request = DistributeRequest(connection,
                ConsumerRequest(states[connection.To], connection.Part, incoming, flows),
                incoming, states, flows);
            total = AddN(total, request);
            if (total is null) return null;
        }
        return total;
    }

    private static Rational? AddN(Rational? a, Rational? b)
        => a is null || b is null ? null : a.Value + b.Value;

    private static Rational? MinN(Rational? a, Rational? b)
        => a is null ? b : b is null ? a : Rational.Min(a.Value, b.Value);

    private static bool NullableEquals(Rational? a, Rational? b)
        => a is null ? b is null : b is not null && a.Value == b.Value;
}
