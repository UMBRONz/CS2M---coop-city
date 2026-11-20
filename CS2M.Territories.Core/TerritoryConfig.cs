namespace CS2M.Territories.Core
{
    /// <summary>
    ///     Session rules. The host's config is authoritative and is sent to clients in the snapshot.
    /// </summary>
    public sealed class TerritoryConfig
    {
        /// <summary>Map tiles per side. Vanilla CS2 maps are 23 x 23 = 529 tiles.</summary>
        public int GridSize { get; set; } = 23;

        /// <summary>Playable map edge length in metres (vanilla: 14,336 m, centred on the origin).</summary>
        public float MapSize { get; set; } = 14336f;

        public int MaxPlayers { get; set; } = 4;

        /// <summary>Edge length of the square start area every player gets (3 = 9 tiles, like vanilla).</summary>
        public int StartAreaSize { get; set; } = 3;

        /// <summary>0 = no limit.</summary>
        public int MaxTilesPerPlayer { get; set; } = 0;

        /// <summary>Money every player starts with in their own treasury.</summary>
        public long StartingMoney { get; set; } = 500_000;

        /// <summary>Price of the first tile bought beyond the start area.</summary>
        public long BaseClaimCost { get; set; } = 100_000;

        /// <summary>Each extra tile already owned makes the next one this much more expensive (0.1 = +10 %).</summary>
        public double ClaimCostGrowth { get; set; } = 0.1;

        /// <summary>If false, a purchase that would make the balance negative is refused.</summary>
        public bool AllowOverdraft { get; set; } = false;
    }
}
