using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2M.Territories.Core
{
    public readonly struct TileCoord : IEquatable<TileCoord>
    {
        public readonly int X;
        public readonly int Y;

        public TileCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(TileCoord other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is TileCoord other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X},{Y})";
        public static bool operator ==(TileCoord a, TileCoord b) => a.Equals(b);
        public static bool operator !=(TileCoord a, TileCoord b) => !a.Equals(b);
    }

    /// <summary>
    ///     Plain data: who owns which tile and how much money each player has.
    ///     The host mutates it through <see cref="TerritoryAuthority" />; clients keep a replica
    ///     and only change it by applying events that came from the host.
    /// </summary>
    public sealed class TerritoryState
    {
        // Not -1: CS2M gives the host player the id -1.
        public const int NoOwner = int.MinValue;

        private readonly int[] _owners;
        private readonly Dictionary<int, long> _balances = new Dictionary<int, long>();

        public TerritoryConfig Config { get; }

        public TerritoryState(TerritoryConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            _owners = new int[config.GridSize * config.GridSize];
            for (int i = 0; i < _owners.Length; i++)
            {
                _owners[i] = NoOwner;
            }
        }

        public int GridSize => Config.GridSize;

        public IEnumerable<int> Players => _balances.Keys.OrderBy(p => p);

        public bool HasPlayer(int playerId) => _balances.ContainsKey(playerId);

        public bool InBounds(TileCoord t) => t.X >= 0 && t.Y >= 0 && t.X < GridSize && t.Y < GridSize;

        public int OwnerOf(TileCoord t) => InBounds(t) ? _owners[t.Y * GridSize + t.X] : NoOwner;

        public long BalanceOf(int playerId) => _balances.TryGetValue(playerId, out long b) ? b : 0;

        public int TileCount(int playerId) => _owners.Count(o => o == playerId);

        public IEnumerable<TileCoord> TilesOf(int playerId)
        {
            for (int i = 0; i < _owners.Length; i++)
            {
                if (_owners[i] == playerId)
                {
                    yield return new TileCoord(i % GridSize, i / GridSize);
                }
            }
        }

        /// <summary>
        ///     Maps a world position (CS2 x/z in metres, origin at map centre) to a tile.
        ///     Returns false outside the playable map.
        /// </summary>
        public bool TryGetTile(float worldX, float worldZ, out TileCoord tile)
        {
            float half = Config.MapSize / 2f;
            float tileSize = Config.MapSize / GridSize;
            int x = (int)Math.Floor((worldX + half) / tileSize);
            int y = (int)Math.Floor((worldZ + half) / tileSize);
            // The far edge itself belongs to the last tile.
            if (Math.Abs(worldX - half) < 1e-3f) x = GridSize - 1;
            if (Math.Abs(worldZ - half) < 1e-3f) y = GridSize - 1;
            tile = new TileCoord(x, y);
            return InBounds(tile);
        }

        /// <summary>World-space centre of a tile, for UI markers.</summary>
        public void TileCenter(TileCoord t, out float worldX, out float worldZ)
        {
            float tileSize = Config.MapSize / GridSize;
            float half = Config.MapSize / 2f;
            worldX = -half + (t.X + 0.5f) * tileSize;
            worldZ = -half + (t.Y + 0.5f) * tileSize;
        }

        public void Apply(TerritoryEvent e)
        {
            switch (e)
            {
                case PlayerJoinedEvent j:
                    _balances[j.PlayerId] = j.Balance;
                    break;
                case TileOwnerChangedEvent t:
                    _owners[t.Tile.Y * GridSize + t.Tile.X] = t.NewOwner;
                    break;
                case BalanceChangedEvent b:
                    _balances[b.PlayerId] = b.NewBalance;
                    break;
                default:
                    throw new ArgumentException($"Unknown event {e?.GetType().Name}");
            }
        }

        public TerritorySnapshot ToSnapshot()
        {
            return new TerritorySnapshot
            {
                Config = Config,
                Owners = (int[])_owners.Clone(),
                Balances = new Dictionary<int, long>(_balances)
            };
        }

        public static TerritoryState FromSnapshot(TerritorySnapshot s)
        {
            var state = new TerritoryState(s.Config);
            if (s.Owners.Length != state._owners.Length)
            {
                throw new ArgumentException("Snapshot grid size does not match its config");
            }

            Array.Copy(s.Owners, state._owners, s.Owners.Length);
            foreach (KeyValuePair<int, long> kv in s.Balances)
            {
                state._balances[kv.Key] = kv.Value;
            }

            return state;
        }
    }

    /// <summary>Full state for late join and for the save sidecar file.</summary>
    public sealed class TerritorySnapshot
    {
        public TerritoryConfig Config { get; set; }
        public int[] Owners { get; set; }
        public Dictionary<int, long> Balances { get; set; }
    }
}
