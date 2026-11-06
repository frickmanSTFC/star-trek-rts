using System.Reflection;
using System.Runtime.CompilerServices;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// The state hash covers everything two peers must agree on: the next-id counters (two games that agree on everything
/// else still split on the next vehicle or building) and the commands waiting in the queue (a peer that missed one shows
/// at once), and every command type has a stable hash of its own.
/// </summary>
public class StateHashTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static IEnumerable<FieldInfo> Counters(Type t) =>
        t.GetFields(Private).Where(f => f.FieldType == typeof(int) && f.Name.StartsWith("_next") && f.Name.EndsWith("Id"));

    [Fact]
    public void EveryNextIdCounterIsInTheHash()
    {
        var sim = CommandFuzzTests.TwoPlayersLanded();
        var owners = new (Type Type, object Instance)[] { (typeof(Simulation), sim), (typeof(PlanetSurface), sim.Surface!) };
        var checkedNames = new List<string>();
        foreach (var (type, instance) in owners)
            foreach (var f in Counters(type))
            {
                var before = sim.StateHash();
                var value = (int)f.GetValue(instance)!;
                f.SetValue(instance, value + 1);
                Assert.True(before != sim.StateHash(), $"{type.Name}.{f.Name} is not in the state hash");
                f.SetValue(instance, value);
                Assert.Equal(before, sim.StateHash());
                checkedNames.Add(f.Name);
            }
        Assert.Equal(new[] { "_nextBuildingId", "_nextColonyId", "_nextDeliveryId", "_nextPodId", "_nextSatelliteId", "_nextShipId",
            "_nextStructureId", "_nextUnitId" }, checkedNames.OrderBy(n => n, StringComparer.Ordinal));

        // A counter anywhere else in the sim would need its own line above (and in that class's AddToHash).
        var elsewhere = typeof(Simulation).Assembly.GetTypes()
            .Where(t => t != typeof(Simulation) && t != typeof(PlanetSurface))
            .SelectMany(t => Counters(t).Select(f => $"{t.Name}.{f.Name}")).ToList();
        Assert.Empty(elsewhere);
    }

    [Fact]
    public void CommandsWaitingInTheQueueAreInTheHash()
    {
        var a = CommandFuzzTests.TwoPlayersLanded();
        var b = CommandFuzzTests.TwoPlayersLanded();
        Assert.Equal(a.StateHash(), b.StateHash());
        var rover = a.Surface!.Units.First(u => u.Owner == 0).Id;
        var tile = a.Surface.Units.First(u => u.Owner == 0).Tile;

        a.Schedule(a.Tick + 5, new MoveUnitsCommand(0, new[] { rover }, tile));
        Assert.NotEqual(a.StateHash(), b.StateHash());
        b.Schedule(b.Tick + 5, new MoveUnitsCommand(0, new[] { rover }, tile));
        Assert.Equal(a.StateHash(), b.StateHash()); // the same command (a new record, same fields): the same hash

        // Any field, the tick it waits for and the order within a tick count.
        void Differs(Action<Simulation> x, Action<Simulation> y)
        {
            var sx = CommandFuzzTests.TwoPlayersLanded();
            var sy = CommandFuzzTests.TwoPlayersLanded();
            x(sx);
            y(sy);
            Assert.NotEqual(sx.StateHash(), sy.StateHash());
        }
        Differs(s => s.Schedule(s.Tick + 5, new MoveUnitsCommand(0, new[] { rover }, tile)),
            s => s.Schedule(s.Tick + 5, new MoveUnitsCommand(0, new[] { rover }, tile + 1)));
        Differs(s => s.Schedule(s.Tick + 5, new MoveUnitsCommand(0, new[] { rover }, tile)),
            s => s.Schedule(s.Tick + 6, new MoveUnitsCommand(0, new[] { rover }, tile)));
        Differs(s => s.Schedule(s.Tick + 5, new MoveUnitsCommand(0, new[] { rover }, tile)),
            s => s.Schedule(s.Tick + 5, new MoveUnitsCommand(1, new[] { rover }, tile)));
        Differs(s => s.Schedule(s.Tick + 5, new PlaceBuildingCommand(0, "storage_container", tile, 1)),
            s => s.Schedule(s.Tick + 5, new PlaceBuildingCommand(0, "storage_container", tile, 2)));
        Differs(s => s.Schedule(s.Tick + 5, new PlaceBuildingCommand(0, "storage_container", tile, 1)),
            s => s.Schedule(s.Tick + 5, new PlaceBuildingCommand(0, "ore_refinery", tile, 1)));
        Differs(s => s.Schedule(s.Tick + 5, new CancelBuildingCommand(0, 3)),
            s => s.Schedule(s.Tick + 5, new UpgradeCommand(0, 3))); // same fields, different type
        Differs(s =>
            {
                s.Schedule(s.Tick + 5, new CancelBuildingCommand(0, 3));
                s.Schedule(s.Tick + 5, new UpgradeCommand(0, 3));
            },
            s =>
            {
                s.Schedule(s.Tick + 5, new UpgradeCommand(0, 3));
                s.Schedule(s.Tick + 5, new CancelBuildingCommand(0, 3));
            });
        Differs(s => s.Schedule(s.Tick + 5, new FakeCommand(0, 1)), s => s.Schedule(s.Tick + 5, new FakeDebugCommand(0)));
    }

    private static IEnumerable<Type> CommandTypes() => typeof(Command).Assembly.GetTypes()
        .Where(t => typeof(Command).IsAssignableFrom(t) && !t.IsAbstract);

    [Fact]
    public void CommandHashCoversEveryCommandType()
    {
        var tags = new Dictionary<int, Type>();
        foreach (var t in CommandTypes())
        {
            var c = (Command)RuntimeHelpers.GetUninitializedObject(t);
            var tag = CommandHash.Tag(c);
            Assert.True(tag != CommandHash.UnknownTag, $"{t.Name} has no tag in CommandHash (add one, and its fields)");
            Assert.True(tags.TryAdd(tag, t), $"{t.Name} and {(tags.TryGetValue(tag, out var o) ? o.Name : "")} share tag {tag}");
        }
        Assert.True(tags.Count >= 36);
        Assert.Equal(CommandHash.UnknownTag, CommandHash.Tag(new FakeCommand(0, 0)));
    }

    /// <summary>Every command type with every field missing or zero (what a broken decoder could make) can be queued,
    /// hashed and run without a crash; one with a missing list or string is refused. (All-zero ints can be a valid order,
    /// e.g. a satellite over row 0.)</summary>
    [Fact]
    public void CommandsWithMissingFieldsAreRefusedNotThrown()
    {
        var sim = CommandFuzzTests.TwoPlayersLanded();
        var types = CommandTypes().Concat(new[] { typeof(FakeDebugCommand), typeof(FakeCommand) }).ToList();
        foreach (var t in types)
        {
            var c = (Command)RuntimeHelpers.GetUninitializedObject(t); // Player 0, ids 0, lists and strings null
            sim.Schedule(sim.Tick, c);
            sim.StateHash();
            var refused = sim.RefusedCommands;
            sim.Step();
            if (CommandChecks.Malformed(c) != null || c is FakeCommand or FakeDebugCommand)
                Assert.True(sim.RefusedCommands == refused + 1, $"{t.Name} with missing fields wasn't refused ({sim.LastRefusal})");
        }
        Assert.Contains("unknown command", sim.LastRefusal ?? "", StringComparison.Ordinal);
        Assert.Empty(CommandFuzzTests.Problems(sim));
    }

    [Fact]
    public void TheMapsMutableAndGeneratedTablesAreInTheHash()
    {
        var sim = CommandFuzzTests.TwoPlayersLanded();
        var map = sim.Surface!.Map;
        var tile = Enumerable.Range(0, map.Grid.Count).First(t => map.DepositResource[t] >= 0);
        var before = sim.StateHash();
        map.DepositStart[tile] += Fix64.One; // what a deep-core mine reopens a share of
        Assert.NotEqual(before, sim.StateHash());
        map.DepositStart[tile] -= Fix64.One;
        map.Elevation[tile] += Fix64.One;
        Assert.NotEqual(before, sim.StateHash());
        map.Elevation[tile] -= Fix64.One;
        Assert.Equal(before, sim.StateHash());
    }
}
