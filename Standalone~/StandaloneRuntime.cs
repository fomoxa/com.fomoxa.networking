using System;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking.Standalone
{
    public static class StandaloneRuntime
    {
        public static NetworkRuntime Create(FomoxaRegistry registry, NetworkSettings settings, ITransportFactory transport, StandalonePrefabs prefabs, StandaloneBehaviours behaviours, ISceneFiles scenes, Func<TimeSpan> clock, NetworkLog log)
        {
            if (prefabs == null)
            {
                throw new ArgumentNullException(nameof(prefabs));
            }

            if (behaviours == null)
            {
                throw new ArgumentNullException(nameof(behaviours));
            }

            if (scenes == null)
            {
                throw new ArgumentNullException(nameof(scenes));
            }

            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            var clientEntities = new StandaloneClientEntityBackend(prefabs);
            var backends = new NetworkBackends(
                new StandaloneServerEntityBackend(prefabs),
                new StandaloneServerSceneHost(registry, scenes, behaviours),
                clientEntities,
                new StandaloneClientSceneHost(registry, scenes, behaviours, clientEntities, log),
                new StandalonePredictionBackend());
            return new NetworkRuntime(registry, settings, transport, backends, clock, log);
        }
    }
}
