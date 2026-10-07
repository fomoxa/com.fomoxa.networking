using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Standalone
{
    internal static class StandaloneSceneObjects
    {
        public static SceneFile Read(FomoxaRegistry registry, ISceneFiles files, uint sceneId)
        {
            SceneFile file = SceneFileFormat.Read(registry, files.Read(sceneId));
            if (file.SceneId != sceneId)
            {
                throw new InvalidDataException($"the scene file of scene 0x{sceneId:X8} describes scene 0x{file.SceneId:X8}");
            }

            return file;
        }

        public static List<StandaloneEntity> Build(SceneFile file, StandaloneBehaviours behaviours)
        {
            uint sceneId = file.SceneId;
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
                var entity = new StandaloneEntity(0, sceneObject.SceneObjectId, sceneObject.Fingerprint, sceneBehaviours)
                {
                    SceneId = sceneId,
                    Position = new Vector3(pose.PositionX, pose.PositionY, pose.PositionZ),
                    Rotation = new Quaternion(pose.RotationX, pose.RotationY, pose.RotationZ, pose.RotationW),
                    Scale = new Vector3(pose.ScaleX, pose.ScaleY, pose.ScaleZ),
                };
                entity.SetFileBodies(
                    SceneFileGeometry.TryGetBody(sceneObject, out BodyDesc body) ? body : (BodyDesc?)null,
                    SceneFileGeometry.TryGetBody2D(sceneObject, out BodyDesc2D body2D) ? body2D : (BodyDesc2D?)null);
                built.Add(entity);
            }

            return built;
        }
    }
}
