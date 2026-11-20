using Unity.Entities;

namespace CS2M.Networking
{
    public partial class NetworkingSystem : SystemBase
    {

        private ConnectionConfig _config;
        private int _maxPlayers;

        protected override void OnCreate()
        {
            base.OnCreate();
            Territories.TerritoriesBridge.Init();
        }

        protected override void OnUpdate()
        {
            NetworkInterface.Instance.OnUpdate();
        }
    }
}