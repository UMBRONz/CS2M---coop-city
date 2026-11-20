using System.Collections.Generic;
using Colossal.Mathematics;
using CS2M.API;
using CS2M.Territories.Core;
using Game;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2M.BaseGame.Territories
{
    /// <summary>
    ///     Stops the local player from building, zoning or bulldozing outside their own land, and from
    ///     building something their own treasury cannot pay for.
    ///
    ///     How: every tool shows its preview as entities with a <see cref="Temp" /> component. After the
    ///     game's own validation, this system tags previews that leave the player's land with
    ///     <see cref="Error" />; the preview turns red and the tool refuses to apply it, the same way it
    ///     refuses an overlapping building.
    ///
    ///     UNVERIFIED (game not installed while this was written, check in game first):
    ///     1. that the tool refuses to apply while any Temp has Error (ToolBaseSystem.GetAllowApply);
    ///     2. that ValidationSystem does not strip our Error again in the same frame (system order);
    ///     3. the field name Temp.m_Cost.
    ///     Footprint check is coarse on purpose: object position, edge curve points, area nodes.
    /// </summary>
    public partial class TerritoryBuildGuardSystem : GameSystemBase
    {
        private EntityQuery _objects;
        private EntityQuery _netNodes;
        private EntityQuery _edges;
        private EntityQuery _areas;
        private EntityQuery _allTemp;

        private TileCoord? _lastWarnedTile;
        private double _lastWarnTime;

        protected override void OnCreate()
        {
            base.OnCreate();
            _objects = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Error>());
            _netNodes = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Game.Net.Node>(),
                ComponentType.Exclude<Error>());
            _edges = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Game.Net.Curve>(),
                ComponentType.Exclude<Error>());
            _areas = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Game.Areas.Node>(),
                ComponentType.Exclude<Error>());
            _allTemp = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.Exclude<Error>());
            RequireForUpdate(_allTemp);
        }

        protected override void OnUpdate()
        {
            if (!TerritorySession.Active) return;

            TerritoryState state = TerritorySession.State;
            int me = TerritorySession.LocalPlayerId;
            var denied = new List<Entity>();
            TileCoord? offending = null;

            void Check(Entity e, float3 p)
            {
                if (state.TryGetTile(p.x, p.z, out TileCoord t) && state.OwnerOf(t) == me) return;
                denied.Add(e);
                offending ??= t;
            }

            using (NativeArray<Entity> es = _objects.ToEntityArray(Allocator.Temp))
                foreach (Entity e in es)
                    Check(e, EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position);

            using (NativeArray<Entity> es = _netNodes.ToEntityArray(Allocator.Temp))
                foreach (Entity e in es)
                    Check(e, EntityManager.GetComponentData<Game.Net.Node>(e).m_Position);

            using (NativeArray<Entity> es = _edges.ToEntityArray(Allocator.Temp))
                foreach (Entity e in es)
                {
                    Bezier4x3 b = EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier;
                    Check(e, b.a);
                    Check(e, MathUtils.Position(b, 0.5f));
                    Check(e, b.d);
                }

            using (NativeArray<Entity> es = _areas.ToEntityArray(Allocator.Temp))
                foreach (Entity e in es)
                    foreach (Game.Areas.Node n in EntityManager.GetBuffer<Game.Areas.Node>(e, true))
                        Check(e, n.m_Position);

            // Treasury check: the whole preview must be affordable from my own money.
            if (denied.Count == 0 && !state.Config.AllowOverdraft)
            {
                long cost = 0;
                using (NativeArray<Temp> temps = _allTemp.ToComponentDataArray<Temp>(Allocator.Temp))
                    foreach (Temp t in temps)
                        cost += t.m_Cost;

                if (cost > state.BalanceOf(me))
                {
                    using (NativeArray<Entity> es = _allTemp.ToEntityArray(Allocator.Temp))
                        denied.AddRange(es);
                    Warn(null, $"Not enough money in your treasury ({state.BalanceOf(me):N0} < {cost:N0}).");
                }
            }

            if (denied.Count == 0) return;
            foreach (Entity e in denied)
            {
                if (!EntityManager.HasComponent<Error>(e)) EntityManager.AddComponent<Error>(e);
            }

            if (offending.HasValue)
            {
                int owner = state.OwnerOf(offending.Value);
                Warn(offending, owner == TerritoryState.NoOwner
                    ? $"Tile {offending.Value} is not yours. Buy it with /claim {offending.Value.X} {offending.Value.Y}."
                    : $"Tile {offending.Value} belongs to mayor #{owner}.");
            }
        }

        // One chat hint per tile every few seconds, not one per frame.
        private void Warn(TileCoord? tile, string message)
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (tile == _lastWarnedTile && now - _lastWarnTime < 5) return;
            _lastWarnedTile = tile;
            _lastWarnTime = now;
            Chat.Instance?.PrintGameMessage(Chat.MessageType.Warning, message);
        }
    }
}
