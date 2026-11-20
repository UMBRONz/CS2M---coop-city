namespace CS2M.Territories.Core
{
    /// <summary>
    ///     A state change decided by the host. Events carry results (new owner, new balance),
    ///     not deltas, so applying one twice or late is harmless.
    /// </summary>
    public abstract class TerritoryEvent
    {
    }

    public sealed class PlayerJoinedEvent : TerritoryEvent
    {
        public int PlayerId { get; set; }
        public long Balance { get; set; }
    }

    public sealed class TileOwnerChangedEvent : TerritoryEvent
    {
        public TileCoord Tile { get; set; }
        public int NewOwner { get; set; }
    }

    public enum BalanceReason
    {
        Start,
        TileClaim,
        Construction,
        MonthlySettlement,
        Transfer
    }

    public sealed class BalanceChangedEvent : TerritoryEvent
    {
        public int PlayerId { get; set; }
        public long NewBalance { get; set; }
        public long Delta { get; set; }
        public BalanceReason Reason { get; set; }
    }

    public enum DenyReason
    {
        None,
        UnknownPlayer,
        SessionFull,
        OutsideMap,
        AlreadyOwned,
        NotAdjacent,
        TileLimit,
        NotOwner,
        InsufficientFunds,
        InvalidAmount
    }
}
