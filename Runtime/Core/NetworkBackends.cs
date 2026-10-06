using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;

namespace Fomoxa.Networking
{
    public sealed class NetworkBackends
    {
        public NetworkBackends(IServerEntityBackend serverEntities, IServerSceneHost serverScenes, IClientEntityBackend clientEntities, ISceneHost clientScenes, IClientPredictionBackend prediction)
        {
            ServerEntities = serverEntities;
            ServerScenes = serverScenes;
            ClientEntities = clientEntities;
            ClientScenes = clientScenes;
            Prediction = prediction;
        }

        public IServerEntityBackend ServerEntities { get; }

        public IServerSceneHost ServerScenes { get; }

        public IClientEntityBackend ClientEntities { get; }

        public ISceneHost ClientScenes { get; }

        public IClientPredictionBackend Prediction { get; }
    }
}
