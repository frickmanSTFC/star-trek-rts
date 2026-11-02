using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

public sealed class Player
{
    public int Slot { get; }
    public string FactionId { get; }
    public ResourceBag Stock { get; }

    internal Player(int slot, string factionId, ResourceBag stock)
    {
        Slot = slot;
        FactionId = factionId;
        Stock = stock;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Slot);
        h.Add(FactionId);
        Stock.AddToHash(ref h);
    }
}
