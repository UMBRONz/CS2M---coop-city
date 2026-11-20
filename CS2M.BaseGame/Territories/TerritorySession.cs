using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CS2M.API;
using CS2M.API.Commands;
using CS2M.Territories.Core;

namespace CS2M.BaseGame.Territories
{
    /// <summary>
    ///     The Territories mode for one multiplayer session.
    ///     Host: owns a <see cref="TerritoryAuthority" />, decides every claim and payment, broadcasts results.
    ///     Client: keeps a replica <see cref="TerritoryState" /> fed only by host events, sends requests.
    ///     No game (ECS) code here; the build guard system reads <see cref="State" /> and <see cref="LocalPlayerId" />.
    /// </summary>
    public static class TerritorySession
    {
        public const int HostPlayerId = -1;
        public const int UnknownPlayerId = int.MinValue;

        private static TerritoryAuthority _authority;

        public static TerritoryState State { get; private set; }
        public static int LocalPlayerId { get; private set; } = UnknownPlayerId;
        public static bool IsHost => _authority != null;
        public static bool Active => State != null && LocalPlayerId != UnknownPlayerId;

        /// <summary>Raised on a client when the host told it its player id (the bridge copies it into LocalPlayer).</summary>
        public static event Action<int> LocalPlayerIdAssigned;

        /// <summary>Raised whenever the state changed (for UI refresh).</summary>
        public static event Action Changed;

        // ---------- lifecycle ----------

        public static void StartAsHost(TerritoryConfig config, TerritorySnapshot restored = null)
        {
            State = restored != null ? TerritoryState.FromSnapshot(restored) : new TerritoryState(config);
            _authority = new TerritoryAuthority(State);
            LocalPlayerId = HostPlayerId;
            _authority.AddPlayer(HostPlayerId); // no-op if restored from a save that already has the host
            Changed?.Invoke();
        }

        public static void StartAsClient()
        {
            _authority = null;
            State = null; // filled by the snapshot
            LocalPlayerId = UnknownPlayerId;
        }

        public static void Stop()
        {
            _authority = null;
            State = null;
            LocalPlayerId = UnknownPlayerId;
            Changed?.Invoke();
        }

        // ---------- host side ----------

        /// <summary>Host: a client finished connecting. Give them land and send them the state.</summary>
        public static void OnPlayerConnected(int playerId)
        {
            if (!IsHost) return;

            TerritoryResult r = _authority.AddPlayer(playerId);
            if (!r.Ok)
            {
                Chat.Instance?.PrintGameMessage(Chat.MessageType.Warning,
                    $"Territories: no land left for player {playerId} ({r.Reason}). They can only watch.");
            }

            // Everyone else learns about the new mayor; the new mayor gets the full picture.
            Broadcast(r);
            Command.SendToPlayer?.Invoke(playerId, BuildSnapshot(playerId));
            Changed?.Invoke();
        }

        public static void HandleClaimOnHost(int playerId, TileCoord tile)
        {
            if (!IsHost) return;
            Publish(playerId, _authority.TryClaim(playerId, tile));
        }

        public static void HandleTransferOnHost(int playerId, int target, long amount)
        {
            if (!IsHost) return;
            Publish(playerId, _authority.Transfer(playerId, target, amount));
        }

        /// <summary>Host: monthly books from the game layer (net income per tile).</summary>
        public static void SettleMonthOnHost(IDictionary<TileCoord, long> netByTile)
        {
            if (!IsHost) return;
            Publish(HostPlayerId, _authority.SettleMonth(netByTile));
        }

        public static TerritorySnapshot SnapshotForSave() => State?.ToSnapshot();

        // ---------- client side ----------

        public static void ApplySnapshot(TerritorySnapshotCommand cmd)
        {
            if (IsHost) return;

            var config = new TerritoryConfig
            {
                GridSize = cmd.GridSize,
                MapSize = cmd.MapSize,
                MaxPlayers = cmd.MaxPlayers,
                StartAreaSize = cmd.StartAreaSize,
                MaxTilesPerPlayer = cmd.MaxTilesPerPlayer,
                StartingMoney = cmd.StartingMoney,
                BaseClaimCost = cmd.BaseClaimCost,
                ClaimCostGrowth = cmd.ClaimCostGrowth,
                AllowOverdraft = cmd.AllowOverdraft
            };
            var balances = new Dictionary<int, long>();
            for (int i = 0; i < cmd.PlayerIds.Length; i++) balances[cmd.PlayerIds[i]] = cmd.Balances[i];

            State = TerritoryState.FromSnapshot(new TerritorySnapshot
            {
                Config = config, Owners = cmd.Owners, Balances = balances
            });
            LocalPlayerId = cmd.YourPlayerId;
            LocalPlayerIdAssigned?.Invoke(LocalPlayerId);
            Chat.Instance?.PrintGameMessage(
                $"Territories: you are mayor #{LocalPlayerId} with {State.TileCount(LocalPlayerId)} tiles. Type /help.");
            Changed?.Invoke();
        }

        public static void ApplyEvents(TerritoryEventsCommand cmd)
        {
            if (IsHost || State == null) return; // the host applied them when deciding
            foreach (TerritoryEvent e in FromWire(cmd)) State.Apply(e);
            Changed?.Invoke();
        }

        public static void ShowDenied(TerritoryDeniedCommand cmd)
        {
            if (cmd.TargetPlayer != LocalPlayerId) return;
            string where = cmd.X >= 0 ? $" tile ({cmd.X},{cmd.Y})" : "";
            string cost = cmd.Cost > 0 ? $", costs {cmd.Cost:N0}" : "";
            Chat.Instance?.PrintGameMessage(Chat.MessageType.Warning,
                $"Refused{where}: {(DenyReason)cmd.Reason}{cost}");
        }

        // ---------- local player actions (chat now, UI later) ----------

        /// <returns>true if the text was a territory command and must not be sent as chat.</returns>
        public static bool TryHandleChat(string text)
        {
            ChatCommand c = ChatCommandParser.Parse(text);
            if (c.Kind == ChatCommandKind.None) return false;

            if (!Active && c.Kind != ChatCommandKind.Help)
            {
                Chat.Instance?.PrintGameMessage(Chat.MessageType.Warning, "Territories: not in a multiplayer session.");
                return true;
            }

            switch (c.Kind)
            {
                case ChatCommandKind.Help:
                    Chat.Instance?.PrintGameMessage(ChatCommandParser.HelpText);
                    break;
                case ChatCommandKind.Land:
                    Chat.Instance?.PrintGameMessage(DescribeLand());
                    break;
                case ChatCommandKind.Money:
                    Chat.Instance?.PrintGameMessage(string.Join(" | ",
                        State.Players.Select(p => $"#{p}{(p == LocalPlayerId ? " (you)" : "")}: {State.BalanceOf(p):N0}")));
                    break;
                case ChatCommandKind.Claim:
                    RequestClaim(new TileCoord(c.X, c.Y));
                    break;
                case ChatCommandKind.Give:
                    RequestTransfer(c.TargetPlayer, c.Amount);
                    break;
                case ChatCommandKind.Invalid:
                    Chat.Instance?.PrintGameMessage(Chat.MessageType.Warning, c.Error);
                    break;
            }

            return true;
        }

        public static void RequestClaim(TileCoord tile)
        {
            if (IsHost) HandleClaimOnHost(LocalPlayerId, tile);
            else Command.SendToServer?.Invoke(new TerritoryClaimRequestCommand { X = tile.X, Y = tile.Y });
        }

        public static void RequestTransfer(int target, long amount)
        {
            if (IsHost) HandleTransferOnHost(LocalPlayerId, target, amount);
            else Command.SendToServer?.Invoke(new TerritoryTransferRequestCommand { TargetPlayer = target, Amount = amount });
        }

        public static string DescribeLand()
        {
            var probe = new TerritoryAuthority(TerritoryState.FromSnapshot(State.ToSnapshot())); // read-only copy
            List<TileCoord> mine = State.TilesOf(LocalPlayerId).ToList();
            List<TileCoord> buyable = Enumerable.Range(0, State.GridSize)
                .SelectMany(y => Enumerable.Range(0, State.GridSize).Select(x => new TileCoord(x, y)))
                .Where(t => State.OwnerOf(t) == TerritoryState.NoOwner && probe.TouchesOwnLand(LocalPlayerId, t))
                .ToList();
            var sb = new StringBuilder();
            sb.Append($"You own {mine.Count} tiles: {string.Join(" ", mine)}. ");
            sb.Append($"Next tile costs {probe.ClaimCost(LocalPlayerId):N0}. Buyable: {string.Join(" ", buyable)}");
            return sb.ToString();
        }

        // ---------- wire conversion ----------

        private static void Publish(int requester, TerritoryResult r)
        {
            if (r.Ok)
            {
                Broadcast(r);
                Changed?.Invoke();
                return;
            }

            var denied = new TerritoryDeniedCommand
            {
                TargetPlayer = requester,
                Reason = (int)r.Reason,
                X = r.Tile?.X ?? -1,
                Y = r.Tile?.Y ?? -1,
                Cost = r.Cost
            };
            if (requester == LocalPlayerId) ShowDenied(denied);
            else Command.SendToClients?.Invoke(denied);
        }

        private static void Broadcast(TerritoryResult r)
        {
            if (r.Events.Count == 0) return;
            Command.SendToClients?.Invoke(ToWire(r.Events));
        }

        private static TerritorySnapshotCommand BuildSnapshot(int forPlayer)
        {
            TerritorySnapshot s = State.ToSnapshot();
            int[] ids = s.Balances.Keys.OrderBy(k => k).ToArray();
            return new TerritorySnapshotCommand
            {
                YourPlayerId = forPlayer,
                GridSize = s.Config.GridSize,
                MapSize = s.Config.MapSize,
                MaxPlayers = s.Config.MaxPlayers,
                StartAreaSize = s.Config.StartAreaSize,
                MaxTilesPerPlayer = s.Config.MaxTilesPerPlayer,
                StartingMoney = s.Config.StartingMoney,
                BaseClaimCost = s.Config.BaseClaimCost,
                ClaimCostGrowth = s.Config.ClaimCostGrowth,
                AllowOverdraft = s.Config.AllowOverdraft,
                Owners = s.Owners,
                PlayerIds = ids,
                Balances = ids.Select(id => s.Balances[id]).ToArray()
            };
        }

        // Events are kept in their original order inside each list; the three kinds never depend
        // on each other's order (a join sets the balance, tiles set owners, balances set balances),
        // except a join must come before that player's balance changes, which applying joins first ensures.
        public static TerritoryEventsCommand ToWire(IEnumerable<TerritoryEvent> events)
        {
            var cmd = new TerritoryEventsCommand();
            foreach (TerritoryEvent e in events)
            {
                switch (e)
                {
                    case PlayerJoinedEvent j:
                        cmd.JoinedPlayers.Add(j.PlayerId);
                        cmd.JoinedBalances.Add(j.Balance);
                        break;
                    case TileOwnerChangedEvent t:
                        cmd.Tiles.Add(new TileChange { X = t.Tile.X, Y = t.Tile.Y, Owner = t.NewOwner });
                        break;
                    case BalanceChangedEvent b:
                        cmd.Balances.Add(new BalanceChange
                        {
                            PlayerId = b.PlayerId, NewBalance = b.NewBalance, Delta = b.Delta, Reason = (int)b.Reason
                        });
                        break;
                }
            }

            return cmd;
        }

        public static IEnumerable<TerritoryEvent> FromWire(TerritoryEventsCommand cmd)
        {
            for (int i = 0; i < cmd.JoinedPlayers.Count; i++)
                yield return new PlayerJoinedEvent { PlayerId = cmd.JoinedPlayers[i], Balance = cmd.JoinedBalances[i] };
            foreach (TileChange t in cmd.Tiles)
                yield return new TileOwnerChangedEvent { Tile = new TileCoord(t.X, t.Y), NewOwner = t.Owner };
            foreach (BalanceChange b in cmd.Balances)
                yield return new BalanceChangedEvent
                {
                    PlayerId = b.PlayerId, NewBalance = b.NewBalance, Delta = b.Delta, Reason = (BalanceReason)b.Reason
                };
        }
    }
}
