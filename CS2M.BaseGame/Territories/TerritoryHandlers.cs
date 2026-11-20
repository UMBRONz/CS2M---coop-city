using CS2M.API.Commands;
using CS2M.Territories.Core;

// Found by reflection (CommandInternal.RefreshModel scans the BaseGame assembly for CommandHandler types).
// SenderId on host-side handlers is stamped by the host from the network peer, not trusted from the client.
namespace CS2M.BaseGame.Territories
{
    public abstract class TerritoryHandler<T> : CommandHandler<T> where T : CommandBase
    {
        protected TerritoryHandler()
        {
            RelayOnServer = false; // the host answers with its own events instead
            TransactionCmd = false;
        }
    }

    public class TerritoryClaimRequestHandler : TerritoryHandler<TerritoryClaimRequestCommand>
    {
        protected override void Handle(TerritoryClaimRequestCommand c)
        {
            TerritorySession.HandleClaimOnHost(c.SenderId, new TileCoord(c.X, c.Y));
        }
    }

    public class TerritoryTransferRequestHandler : TerritoryHandler<TerritoryTransferRequestCommand>
    {
        protected override void Handle(TerritoryTransferRequestCommand c)
        {
            TerritorySession.HandleTransferOnHost(c.SenderId, c.TargetPlayer, c.Amount);
        }
    }

    public class TerritorySnapshotHandler : TerritoryHandler<TerritorySnapshotCommand>
    {
        protected override void Handle(TerritorySnapshotCommand c) => TerritorySession.ApplySnapshot(c);
    }

    public class TerritoryEventsHandler : TerritoryHandler<TerritoryEventsCommand>
    {
        protected override void Handle(TerritoryEventsCommand c) => TerritorySession.ApplyEvents(c);
    }

    public class TerritoryDeniedHandler : TerritoryHandler<TerritoryDeniedCommand>
    {
        protected override void Handle(TerritoryDeniedCommand c) => TerritorySession.ShowDenied(c);
    }
}
