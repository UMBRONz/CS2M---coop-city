using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2M.Territories.Core
{
    public sealed class TerritoryResult
    {
        public bool Ok => Reason == DenyReason.None;
        public DenyReason Reason { get; private set; }

        /// <summary>For a denied build or claim: the tile that caused it, if any.</summary>
        public TileCoord? Tile { get; private set; }

        public long Cost { get; private set; }

        /// <summary>Events to apply locally and broadcast to clients (empty when denied).</summary>
        public List<TerritoryEvent> Events { get; } = new List<TerritoryEvent>();

        internal static TerritoryResult Deny(DenyReason reason, TileCoord? tile = null, long cost = 0)
        {
            return new TerritoryResult { Reason = reason, Tile = tile, Cost = cost };
        }

        internal static TerritoryResult Allow(long cost = 0)
        {
            return new TerritoryResult { Reason = DenyReason.None, Cost = cost };
        }
    }

    /// <summary>
    ///     Host-side rules. Every decision about territory and money is made here, on the host,
    ///     and leaves as events. Clients ask (claim request, build request) and wait for the answer.
    /// </summary>
    public sealed class TerritoryAuthority
    {
        // Preferred start-area centres as fractions of the map, one per player slot.
        // Two players start left and right of the centre with neutral land between them.
        private static readonly (double x, double y)[] Anchors =
        {
            (0.27, 0.5), (0.73, 0.5), (0.5, 0.27), (0.5, 0.73),
            (0.27, 0.27), (0.73, 0.73), (0.27, 0.73), (0.73, 0.27)
        };

        public TerritoryState State { get; }
        public TerritoryConfig Config => State.Config;

        public TerritoryAuthority(TerritoryConfig config) : this(new TerritoryState(config))
        {
        }

        public TerritoryAuthority(TerritoryState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        public int StartTiles => Config.StartAreaSize * Config.StartAreaSize;

        /// <summary>
        ///     Adds a player with a start area and starting money. A player who was already in the
        ///     session (rejoin, or loaded from the save sidecar) keeps their land and money.
        /// </summary>
        public TerritoryResult AddPlayer(int playerId)
        {
            if (State.HasPlayer(playerId))
            {
                return TerritoryResult.Allow();
            }

            int slot = State.Players.Count();
            if (slot >= Config.MaxPlayers)
            {
                return TerritoryResult.Deny(DenyReason.SessionFull);
            }

            if (!TryFindStartArea(slot, out TileCoord corner))
            {
                return TerritoryResult.Deny(DenyReason.SessionFull);
            }

            TerritoryResult result = TerritoryResult.Allow();
            Emit(result, new PlayerJoinedEvent { PlayerId = playerId, Balance = Config.StartingMoney });
            for (int dy = 0; dy < Config.StartAreaSize; dy++)
            {
                for (int dx = 0; dx < Config.StartAreaSize; dx++)
                {
                    Emit(result, new TileOwnerChangedEvent
                    {
                        Tile = new TileCoord(corner.X + dx, corner.Y + dy),
                        NewOwner = playerId
                    });
                }
            }

            return result;
        }

        public long ClaimCost(int playerId)
        {
            int extra = Math.Max(0, State.TileCount(playerId) - StartTiles);
            return (long)Math.Round(Config.BaseClaimCost * (1.0 + Config.ClaimCostGrowth * extra));
        }

        /// <summary>Buy a free tile that touches (shares an edge with) the player's land.</summary>
        public TerritoryResult TryClaim(int playerId, TileCoord tile)
        {
            if (!State.HasPlayer(playerId)) return TerritoryResult.Deny(DenyReason.UnknownPlayer);
            if (!State.InBounds(tile)) return TerritoryResult.Deny(DenyReason.OutsideMap, tile);
            if (State.OwnerOf(tile) != TerritoryState.NoOwner) return TerritoryResult.Deny(DenyReason.AlreadyOwned, tile);
            if (!TouchesOwnLand(playerId, tile)) return TerritoryResult.Deny(DenyReason.NotAdjacent, tile);
            if (Config.MaxTilesPerPlayer > 0 && State.TileCount(playerId) >= Config.MaxTilesPerPlayer)
            {
                return TerritoryResult.Deny(DenyReason.TileLimit, tile);
            }

            long cost = ClaimCost(playerId);
            if (!CanAfford(playerId, cost)) return TerritoryResult.Deny(DenyReason.InsufficientFunds, tile, cost);

            TerritoryResult result = TerritoryResult.Allow(cost);
            Emit(result, new TileOwnerChangedEvent { Tile = tile, NewOwner = playerId });
            Charge(result, playerId, cost, BalanceReason.TileClaim);
            return result;
        }

        /// <summary>
        ///     Checks a build action (road, zone, building, bulldoze) whose footprint is given as
        ///     world points, and charges its cost to the builder. Every point must be on the
        ///     builder's own land.
        /// </summary>
        public TerritoryResult CheckBuild(int playerId, IEnumerable<(float x, float z)> footprint, long cost)
        {
            if (!State.HasPlayer(playerId)) return TerritoryResult.Deny(DenyReason.UnknownPlayer);
            if (cost < 0) return TerritoryResult.Deny(DenyReason.InvalidAmount);

            foreach ((float x, float z) in footprint)
            {
                if (!State.TryGetTile(x, z, out TileCoord tile)) return TerritoryResult.Deny(DenyReason.OutsideMap);
                if (State.OwnerOf(tile) != playerId) return TerritoryResult.Deny(DenyReason.NotOwner, tile);
            }

            if (!CanAfford(playerId, cost)) return TerritoryResult.Deny(DenyReason.InsufficientFunds, null, cost);

            TerritoryResult result = TerritoryResult.Allow(cost);
            if (cost > 0)
            {
                Charge(result, playerId, cost, BalanceReason.Construction);
            }

            return result;
        }

        /// <summary>Money from one mayor to another (gifts now, loans and trade later).</summary>
        public TerritoryResult Transfer(int fromPlayer, int toPlayer, long amount)
        {
            if (!State.HasPlayer(fromPlayer) || !State.HasPlayer(toPlayer) || fromPlayer == toPlayer)
            {
                return TerritoryResult.Deny(DenyReason.UnknownPlayer);
            }

            if (amount <= 0) return TerritoryResult.Deny(DenyReason.InvalidAmount);
            if (!CanAfford(fromPlayer, amount)) return TerritoryResult.Deny(DenyReason.InsufficientFunds, null, amount);

            TerritoryResult result = TerritoryResult.Allow(amount);
            Charge(result, fromPlayer, amount, BalanceReason.Transfer);
            Credit(result, toPlayer, amount, BalanceReason.Transfer);
            return result;
        }

        /// <summary>
        ///     Monthly books: the game layer measures the net income (taxes minus upkeep) of
        ///     everything standing on each tile; each tile's net goes to the tile's owner.
        ///     Unowned tiles are ignored. Settlement may push a balance below zero.
        /// </summary>
        public TerritoryResult SettleMonth(IDictionary<TileCoord, long> netByTile)
        {
            var perPlayer = new Dictionary<int, long>();
            foreach (KeyValuePair<TileCoord, long> kv in netByTile)
            {
                int owner = State.OwnerOf(kv.Key);
                if (owner == TerritoryState.NoOwner || !State.HasPlayer(owner)) continue;
                perPlayer.TryGetValue(owner, out long sum);
                perPlayer[owner] = sum + kv.Value;
            }

            TerritoryResult result = TerritoryResult.Allow();
            foreach (KeyValuePair<int, long> kv in perPlayer.OrderBy(k => k.Key))
            {
                if (kv.Value != 0)
                {
                    Credit(result, kv.Key, kv.Value, BalanceReason.MonthlySettlement);
                }
            }

            return result;
        }

        public bool TouchesOwnLand(int playerId, TileCoord tile)
        {
            return State.OwnerOf(new TileCoord(tile.X + 1, tile.Y)) == playerId
                   || State.OwnerOf(new TileCoord(tile.X - 1, tile.Y)) == playerId
                   || State.OwnerOf(new TileCoord(tile.X, tile.Y + 1)) == playerId
                   || State.OwnerOf(new TileCoord(tile.X, tile.Y - 1)) == playerId;
        }

        private bool CanAfford(int playerId, long cost)
        {
            return Config.AllowOverdraft || State.BalanceOf(playerId) >= cost;
        }

        private void Charge(TerritoryResult result, int playerId, long amount, BalanceReason reason)
        {
            Credit(result, playerId, -amount, reason);
        }

        private void Credit(TerritoryResult result, int playerId, long delta, BalanceReason reason)
        {
            Emit(result, new BalanceChangedEvent
            {
                PlayerId = playerId,
                Delta = delta,
                NewBalance = State.BalanceOf(playerId) + delta,
                Reason = reason
            });
        }

        // Events are applied to the host state immediately, so later checks in the same call
        // (and the NewBalance of the next event) see the updated values.
        private void Emit(TerritoryResult result, TerritoryEvent e)
        {
            State.Apply(e);
            result.Events.Add(e);
        }

        private bool TryFindStartArea(int slot, out TileCoord corner)
        {
            int size = Config.StartAreaSize;
            int max = Config.GridSize - size;
            if (max < 0)
            {
                corner = default;
                return false;
            }

            if (slot < Anchors.Length)
            {
                (double fx, double fy) = Anchors[slot];
                int cx = (int)Math.Round(fx * (Config.GridSize - 1)) - size / 2;
                int cy = (int)Math.Round(fy * (Config.GridSize - 1)) - size / 2;
                corner = new TileCoord(Math.Min(Math.Max(cx, 0), max), Math.Min(Math.Max(cy, 0), max));
                if (IsFreeWithMargin(corner, size)) return true;
            }

            // Fallback: the first free square, scanning from the centre outwards.
            int mid = max / 2;
            foreach (TileCoord c in Enumerable.Range(0, max + 1)
                         .SelectMany(y => Enumerable.Range(0, max + 1).Select(x => new TileCoord(x, y)))
                         .OrderBy(c => Math.Abs(c.X - mid) + Math.Abs(c.Y - mid)))
            {
                if (IsFreeWithMargin(c, size))
                {
                    corner = c;
                    return true;
                }
            }

            corner = default;
            return false;
        }

        // Free square, and no other player's land touching it, so start areas never share a border.
        private bool IsFreeWithMargin(TileCoord corner, int size)
        {
            for (int y = corner.Y - 1; y <= corner.Y + size; y++)
            {
                for (int x = corner.X - 1; x <= corner.X + size; x++)
                {
                    if (State.OwnerOf(new TileCoord(x, y)) != TerritoryState.NoOwner) return false;
                }
            }

            return true;
        }
    }
}
