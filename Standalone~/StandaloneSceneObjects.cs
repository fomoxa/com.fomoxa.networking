using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking.Standalone
{
    internal static class StandaloneSceneObjects
    {
        public static List<StandaloneEntity> Build(FomoxaRegistry registry, ISceneFiles files, StandaloneBehaviours behaviours, uint sceneId)
        {
            SceneFile file = SceneFileFormat.Read(registry, files.Read(sceneId));
            if (file.SceneId != sceneId)
            {
                throw new InvalidDataException($"the scene file of scene 0x{sceneId:X8} describes scene 0x{file.SceneId:X8}");
            }

            var built = new List<StandaloneEntity>(file.Objects.Count);
            var seen = new HashSet<ulong>();
            foreach (SceneFileObject sceneObject in file.Objects)
            {
                if (sceneObject.SceneObjectId == 0 || !seen.Add(sceneObject.SceneObjectId))
                {
                    throw new InvalidDataException($"the scene file of scene 0x{sceneId:X8} has a zero or repeated scene object id 0x{sceneObject.SceneObjectId:X16}");
                }

                var sceneBehaviours = new List<EntityBehaviour>(sceneObject.BehaviourTypes.Count);
                foreach (string typeName in sceneObject.BehaviourTypes)
                {
                    sceneBehaviours.Add(behaviours.Create(typeName));
                }

                SceneFilePose pose = sceneObject.Pose;
                built.Add(new StandaloneEntity(0, sceneObject.SceneObjectId, sceneObject.Fingerprint, sceneBehaviours)
                {
                    SceneId = sceneId,
                    Position = new Vector3(pose.PositionX, pose.PositionY, pose.PositionZ),
                    Rotation = new Quaternion(pose.RotationX, pose.RotationY, pose.RotationZ, pose.RotationW),
                    Scale = new Vector3(pose.ScaleX, pose.ScaleY, pose.ScaleZ),
                });
            }

            return built;
        }
    }
}
