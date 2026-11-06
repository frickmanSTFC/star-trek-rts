using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

public sealed class Ship
{
    public int Id { get; }
    public int Owner { get; }
    public string ClassKey { get; }
    public int HullSize { get; }
    public int MaxHp { get; }
    public int Hp { get; internal set; }

    /// <summary>Map units moved per tick.</summary>
    public Fix64 SpeedPerTick { get; }

    public FixVec2 Position { get; internal set; }

    /// <summary>Unit vector the ship faces; the renderer uses it for orientation.</summary>
    public FixVec2 Heading { get; internal set; }

    public bool HasMoveTarget { get; internal set; }
    public FixVec2 MoveTarget { get; internal set; }

    internal Ship(int id, int owner, string classKey, int hullSize, int maxHp, Fix64 speedPerTick, FixVec2 position, FixVec2 heading)
    {
        Id = id;
        Owner = owner;
        ClassKey = classKey;
        HullSize = hullSize;
        MaxHp = maxHp;
        Hp = maxHp;
        SpeedPerTick = speedPerTick;
        Position = position;
        Heading = heading;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Owner);
        h.Add(ClassKey);
        h.Add(HullSize);
        h.Add(MaxHp);
        h.Add(Hp);
        h.Add(SpeedPerTick);
        h.Add(Position);
        h.Add(Heading);
        h.Add(HasMoveTarget);
        h.Add(MoveTarget);
    }
}
