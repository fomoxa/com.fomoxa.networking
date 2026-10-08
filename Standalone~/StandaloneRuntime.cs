using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking.Standalone
{
    public static class StandaloneRuntime
    {
        public static NetworkRuntime Create(FomoxaRegistry registry, NetworkSettings settings, ITransportFactory transport, StandalonePrefabs prefabs, StandaloneBehaviours behaviours, ISceneFiles scenes, Func<TimeSpan> clock, NetworkLog log, IPhysicsScenes physics = null, IReadOnlyList<uint> bootScenes = null)
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

            var boot = new List<SceneFile>();
            foreach (uint sceneId in bootScenes ?? Array.Empty<uint>())
            {
                if (!scenes.Knows(sceneId))
                {
                    throw new ArgumentException($"boot scene 0x{sceneId:X8} has no scene file", nameof(bootScenes));
                }

                boot.Add(StandaloneSceneObjects.Read(registry, scenes, sceneId));
            }

            foreach (SceneFile file in boot)
            {
                physics?.LoadScene(file);
            }

            var clientEntities = new StandaloneClientEntityBackend(prefabs, physics);
            var backends = new NetworkBackends(
                new StandaloneServerEntityBackend(prefabs, physics, boot),
                new StandaloneServerSceneHost(registry, scenes, behaviours, physics, boot),
                clientEntities,
                new StandaloneClientSceneHost(registry, scenes, behaviours, clientEntities, log, physics, boot),
                new StandalonePredictionBackend(physics));
            var runtime = new NetworkRuntime(registry, settings, transport, backends, clock, log);
            if (physics != null)
            {
                runtime.Physics = physics;
            }

            return runtime;
        }
    }
}
