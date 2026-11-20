using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.BaseGame.Territories;
using CS2M.Networking;
using CS2M.Territories.Core;

namespace CS2M.Territories
{
    /// <summary>
    ///     Connects CS2M's networking events to the Territories session (which lives in CS2M.BaseGame
    ///     and cannot see CS2M's networking types).
    /// </summary>
    public static class TerritoriesBridge
    {
        private static bool _hooked;

        public static void Init()
        {
            if (_hooked) return;
            _hooked = true;

            NetworkInterface.Instance.PlayerConnectedEvent += player =>
                TerritorySession.OnPlayerConnected(player.PlayerId);

            TerritorySession.LocalPlayerIdAssigned += id =>
                NetworkInterface.Instance.LocalPlayer.PlayerId = id;
        }

        public static void OnLocalPlayerTypeChanged(LocalPlayer local, PlayerType type)
        {
            switch (type)
            {
                case PlayerType.SERVER:
                    Command.CurrentRole = MultiplayerRole.Server;
                    local.PlayerId = TerritorySession.HostPlayerId;
                    // TODO(save sidecar): pass the snapshot stored next to the loaded save as "restored".
                    TerritorySession.StartAsHost(new TerritoryConfig());
                    break;
                case PlayerType.CLIENT:
                    Command.CurrentRole = MultiplayerRole.Client;
                    TerritorySession.StartAsClient();
                    break;
                default:
                    Command.CurrentRole = MultiplayerRole.None;
                    TerritorySession.Stop();
                    break;
            }
        }
    }
}
