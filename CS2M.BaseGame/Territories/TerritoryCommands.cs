using System.Collections.Generic;
using CS2M.API.Commands;

// Wire format for the Territories mode. Plain properties and ints only (MessagePack attributeless
// serializes public get/set properties; the core's readonly TileCoord struct is kept off the wire).
namespace CS2M.BaseGame.Territories
{
    /// <summary>Client -> host: "I want to buy this tile."</summary>
    public class TerritoryClaimRequestCommand : CommandBase
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    /// <summary>Client -> host: "send this much of my money to that mayor."</summary>
    public class TerritoryTransferRequestCommand : CommandBase
    {
        public int TargetPlayer { get; set; }
        public long Amount { get; set; }
    }

    /// <summary>Host -> one client: the full state, and which player id that client is.</summary>
    public class TerritorySnapshotCommand : CommandBase
    {
        public int YourPlayerId { get; set; }

        public int GridSize { get; set; }
        public float MapSize { get; set; }
        public int MaxPlayers { get; set; }
        public int StartAreaSize { get; set; }
        public int MaxTilesPerPlayer { get; set; }
        public long StartingMoney { get; set; }
        public long BaseClaimCost { get; set; }
        public double ClaimCostGrowth { get; set; }
        public bool AllowOverdraft { get; set; }

        public int[] Owners { get; set; }
        public int[] PlayerIds { get; set; }
        public long[] Balances { get; set; }
    }

    public class TileChange
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Owner { get; set; }
    }

    public class BalanceChange
    {
        public int PlayerId { get; set; }
        public long NewBalance { get; set; }
        public long Delta { get; set; }
        public int Reason { get; set; }
    }

    /// <summary>Host -> all clients: results of one decision, in order.</summary>
    public class TerritoryEventsCommand : CommandBase
    {
        public List<int> JoinedPlayers { get; set; } = new List<int>();
        public List<long> JoinedBalances { get; set; } = new List<long>();
        public List<TileChange> Tiles { get; set; } = new List<TileChange>();
        public List<BalanceChange> Balances { get; set; } = new List<BalanceChange>();
    }

    /// <summary>Host -> all clients: a request was refused. Only <see cref="TargetPlayer" /> shows it.</summary>
    public class TerritoryDeniedCommand : CommandBase
    {
        public int TargetPlayer { get; set; }
        public int Reason { get; set; }
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;
        public long Cost { get; set; }
    }
}
