using System.Collections.Generic;
using System.Linq;
using CS2M.Territories.Core;
using NUnit.Framework;

namespace CS2M.Territories.Tests
{
    public class TerritoryAuthorityTests
    {
        private const int Host = -1; // CS2M uses -1 for the server player
        private const int Guest = 1;

        private static TerritoryAuthority NewSession(TerritoryConfig config = null)
        {
            var a = new TerritoryAuthority(config ?? new TerritoryConfig());
            Assert.That(a.AddPlayer(Host).Ok);
            Assert.That(a.AddPlayer(Guest).Ok);
            return a;
        }

        [Test]
        public void StartAreas_AreFullSizeAndDoNotTouch()
        {
            var a = new TerritoryAuthority(new TerritoryConfig());
            for (int p = 0; p < 4; p++) Assert.That(a.AddPlayer(p).Ok, $"player {p}");

            for (int p = 0; p < 4; p++)
            {
                Assert.That(a.State.TileCount(p), Is.EqualTo(9));
                Assert.That(a.State.BalanceOf(p), Is.EqualTo(500_000));
                foreach (TileCoord t in a.State.TilesOf(p))
                {
                    foreach (TileCoord n in new[]
                             {
                                 new TileCoord(t.X + 1, t.Y), new TileCoord(t.X - 1, t.Y),
                                 new TileCoord(t.X, t.Y + 1), new TileCoord(t.X, t.Y - 1)
                             })
                    {
                        int o = a.State.OwnerOf(n);
                        Assert.That(o == p || o == TerritoryState.NoOwner, $"player {p} touches {o} at {n}");
                    }
                }
            }
        }

        [Test]
        public void TwoPlayers_StartLeftAndRightOfCentre()
        {
            TerritoryAuthority a = NewSession();
            Assert.That(a.State.TilesOf(Host).Max(t => t.X), Is.LessThan(11));
            Assert.That(a.State.TilesOf(Guest).Min(t => t.X), Is.GreaterThan(11));
        }

        [Test]
        public void SessionFull_WhenMaxPlayersReached()
        {
            var a = new TerritoryAuthority(new TerritoryConfig { MaxPlayers = 2 });
            a.AddPlayer(0);
            a.AddPlayer(1);
            Assert.That(a.AddPlayer(2).Reason, Is.EqualTo(DenyReason.SessionFull));
        }

        [Test]
        public void Rejoin_KeepsLandAndMoney()
        {
            TerritoryAuthority a = NewSession();
            a.Transfer(Guest, Host, 1000);
            TerritoryResult r = a.AddPlayer(Guest);
            Assert.That(r.Ok && r.Events.Count == 0);
            Assert.That(a.State.BalanceOf(Guest), Is.EqualTo(499_000));
        }

        [Test]
        public void Claim_AdjacentTile_ChargesAndTransfersOwnership()
        {
            TerritoryAuthority a = NewSession();
            TileCoord edge = a.State.TilesOf(Host).OrderBy(t => t.X).First();
            var target = new TileCoord(edge.X - 1, edge.Y);

            TerritoryResult r = a.TryClaim(Host, target);

            Assert.That(r.Ok, r.Reason.ToString());
            Assert.That(r.Cost, Is.EqualTo(100_000));
            Assert.That(a.State.OwnerOf(target), Is.EqualTo(Host));
            Assert.That(a.State.BalanceOf(Host), Is.EqualTo(400_000));
        }

        [Test]
        public void Claim_CostGrowsWithEachExtraTile()
        {
            TerritoryAuthority a = NewSession();
            TileCoord edge = a.State.TilesOf(Host).OrderBy(t => t.X).First();
            a.TryClaim(Host, new TileCoord(edge.X - 1, edge.Y));
            Assert.That(a.ClaimCost(Host), Is.EqualTo(110_000));
        }

        [Test]
        public void Claim_Refused_WhenNotAdjacent_Owned_OutsideOrBroke()
        {
            TerritoryAuthority a = NewSession(new TerritoryConfig { StartingMoney = 50_000 });
            TileCoord hostTile = a.State.TilesOf(Host).First();
            TileCoord edge = a.State.TilesOf(Host).OrderBy(t => t.X).First();

            Assert.That(a.TryClaim(Host, new TileCoord(0, 0)).Reason, Is.EqualTo(DenyReason.NotAdjacent));
            Assert.That(a.TryClaim(Guest, hostTile).Reason, Is.EqualTo(DenyReason.AlreadyOwned));
            Assert.That(a.TryClaim(Host, new TileCoord(-1, 5)).Reason, Is.EqualTo(DenyReason.OutsideMap));
            Assert.That(a.TryClaim(Host, new TileCoord(edge.X - 1, edge.Y)).Reason,
                Is.EqualTo(DenyReason.InsufficientFunds));
            Assert.That(a.TryClaim(42, edge).Reason, Is.EqualTo(DenyReason.UnknownPlayer));
        }

        [Test]
        public void Claim_RespectsTileLimit()
        {
            TerritoryAuthority a = NewSession(new TerritoryConfig { MaxTilesPerPlayer = 9 });
            TileCoord edge = a.State.TilesOf(Host).OrderBy(t => t.X).First();
            Assert.That(a.TryClaim(Host, new TileCoord(edge.X - 1, edge.Y)).Reason, Is.EqualTo(DenyReason.TileLimit));
        }

        [Test]
        public void Build_OnOwnLand_Charges_OnNeighbourLand_Refused()
        {
            TerritoryAuthority a = NewSession();
            a.State.TileCenter(a.State.TilesOf(Host).First(), out float hx, out float hz);
            a.State.TileCenter(a.State.TilesOf(Guest).First(), out float gx, out float gz);

            TerritoryResult ok = a.CheckBuild(Host, new[] { (hx, hz) }, 25_000);
            Assert.That(ok.Ok);
            Assert.That(a.State.BalanceOf(Host), Is.EqualTo(475_000));

            // A road from my land into the neighbour's land is refused as a whole, nothing charged.
            TerritoryResult cross = a.CheckBuild(Host, new[] { (hx, hz), (gx, gz) }, 25_000);
            Assert.That(cross.Reason, Is.EqualTo(DenyReason.NotOwner));
            Assert.That(cross.Tile, Is.EqualTo(a.State.TilesOf(Guest).First()));
            Assert.That(cross.Events, Is.Empty);
            Assert.That(a.State.BalanceOf(Host), Is.EqualTo(475_000));
        }

        [Test]
        public void Build_Refused_OnNeutralLand_AndWhenTooExpensive()
        {
            TerritoryAuthority a = NewSession();
            a.State.TileCenter(new TileCoord(11, 11), out float nx, out float nz);
            a.State.TileCenter(a.State.TilesOf(Host).First(), out float hx, out float hz);

            Assert.That(a.CheckBuild(Host, new[] { (nx, nz) }, 0).Reason, Is.EqualTo(DenyReason.NotOwner));
            Assert.That(a.CheckBuild(Host, new[] { (hx, hz) }, 600_000).Reason, Is.EqualTo(DenyReason.InsufficientFunds));
            Assert.That(a.CheckBuild(Host, new[] { (hx, hz) }, -5).Reason, Is.EqualTo(DenyReason.InvalidAmount));
        }

        [Test]
        public void Overdraft_AllowsNegativeBalance()
        {
            TerritoryAuthority a = NewSession(new TerritoryConfig { AllowOverdraft = true });
            a.State.TileCenter(a.State.TilesOf(Host).First(), out float hx, out float hz);
            Assert.That(a.CheckBuild(Host, new[] { (hx, hz) }, 600_000).Ok);
            Assert.That(a.State.BalanceOf(Host), Is.EqualTo(-100_000));
        }

        [Test]
        public void Transfer_MovesMoney()
        {
            TerritoryAuthority a = NewSession();
            Assert.That(a.Transfer(Host, Guest, 200_000).Ok);
            Assert.That(a.State.BalanceOf(Host), Is.EqualTo(300_000));
            Assert.That(a.State.BalanceOf(Guest), Is.EqualTo(700_000));
            Assert.That(a.Transfer(Host, Guest, 0).Reason, Is.EqualTo(DenyReason.InvalidAmount));
            Assert.That(a.Transfer(Host, Host, 10).Reason, Is.EqualTo(DenyReason.UnknownPlayer));
            Assert.That(a.Transfer(Host, Guest, 10_000_000).Reason, Is.EqualTo(DenyReason.InsufficientFunds));
        }

        [Test]
        public void SettleMonth_PaysEachTileToItsOwner()
        {
            TerritoryAuthority a = NewSession();
            TileCoord[] host = a.State.TilesOf(Host).ToArray();
            TileCoord guest = a.State.TilesOf(Guest).First();
            var net = new Dictionary<TileCoord, long>
            {
                [host[0]] = 3_000,
                [host[1]] = -1_000,
                [guest] = -8_000,
                [new TileCoord(11, 11)] = 99_999 // neutral land: nobody gets it
            };

            TerritoryResult r = a.SettleMonth(net);

            Assert.That(r.Ok);
            Assert.That(a.State.BalanceOf(Host), Is.EqualTo(502_000));
            Assert.That(a.State.BalanceOf(Guest), Is.EqualTo(492_000));
        }

        [Test]
        public void WorldToTile_MapsCornersAndCentre()
        {
            var s = new TerritoryState(new TerritoryConfig());
            Assert.That(s.TryGetTile(-7168, -7168, out TileCoord t0) && t0 == new TileCoord(0, 0));
            Assert.That(s.TryGetTile(7168, 7168, out TileCoord t1) && t1 == new TileCoord(22, 22));
            Assert.That(s.TryGetTile(0, 0, out TileCoord c) && c == new TileCoord(11, 11));
            Assert.That(s.TryGetTile(7200, 0, out _), Is.False);

            s.TileCenter(new TileCoord(4, 17), out float x, out float z);
            Assert.That(s.TryGetTile(x, z, out TileCoord back) && back == new TileCoord(4, 17));
        }

        [Test]
        public void Replica_FedWithHostEvents_MatchesHost()
        {
            var config = new TerritoryConfig();
            var host = new TerritoryAuthority(config);
            var replica = new TerritoryState(config);
            var log = new List<TerritoryEvent>();

            log.AddRange(host.AddPlayer(Host).Events);
            log.AddRange(host.AddPlayer(Guest).Events);
            TileCoord edge = host.State.TilesOf(Guest).OrderBy(t => t.X).Last();
            log.AddRange(host.TryClaim(Guest, new TileCoord(edge.X + 1, edge.Y)).Events);
            log.AddRange(host.Transfer(Host, Guest, 1234).Events);
            log.AddRange(host.TryClaim(Host, new TileCoord(0, 0)).Events); // denied: no events

            foreach (TerritoryEvent e in log) replica.Apply(e);

            AssertSameState(host.State, replica);
        }

        [Test]
        public void Snapshot_RoundTrips()
        {
            TerritoryAuthority a = NewSession();
            a.Transfer(Guest, Host, 777);
            TerritoryState copy = TerritoryState.FromSnapshot(a.State.ToSnapshot());
            AssertSameState(a.State, copy);

            // A restored session keeps working: the host can go on making decisions.
            var resumed = new TerritoryAuthority(copy);
            Assert.That(resumed.AddPlayer(Guest).Events, Is.Empty);
            Assert.That(resumed.AddPlayer(2).Ok);
        }

        private static void AssertSameState(TerritoryState expected, TerritoryState actual)
        {
            Assert.That(actual.Players, Is.EqualTo(expected.Players));
            Assert.That(actual.ToSnapshot().Owners, Is.EqualTo(expected.ToSnapshot().Owners));
            foreach (int p in expected.Players)
            {
                Assert.That(actual.BalanceOf(p), Is.EqualTo(expected.BalanceOf(p)), $"balance of {p}");
            }
        }
    }
}
