using System;
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

                List<ChildBodyDesc> children = ChildBodiesOf(sceneObject);
                List<ChildBodyDesc2D> children2D = ChildBodies2DOf(sceneObject);
                var sceneBehaviours = new List<EntityBehaviour>(sceneObject.BehaviourTypes.Count);
                for (int index = 0; index < sceneObject.BehaviourTypes.Count; index++)
                {
                    sceneBehaviours.Add(behaviours.Create(sceneObject.BehaviourTypes[index], SiteOf(sceneObject, index, children.Count, children2D.Count)));
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
                    SceneFileGeometry.TryGetBody2D(sceneObject, out BodyDesc2D body2D) ? body2D : (BodyDesc2D?)null,
                    children,
                    children2D);
                built.Add(entity);
            }

            return built;
        }

        private static List<ChildBodyDesc> ChildBodiesOf(SceneFileObject sceneObject)
        {
            var children = new List<ChildBodyDesc>(sceneObject.ChildBodies.Count);
            for (int index = 0; index < sceneObject.ChildBodies.Count; index++)
            {
                SceneFileChildBody child = sceneObject.ChildBodies[index];
                if (child?.Body == null || child.Body.Kind == 0)
                {
                    throw Invalid(sceneObject, $"child body {index} has no body");
                }

                var localPosition = new Vector3(child.PositionX, child.PositionY, child.PositionZ);
                var localRotation = new Quaternion(child.RotationX, child.RotationY, child.RotationZ, child.RotationW);
                try
                {
                    children.Add(new ChildBodyDesc(SceneFileGeometry.ToDesc(child.Body, localPosition, localRotation), localPosition, localRotation));
                }
                catch (ArgumentException exception)
                {
                    throw Invalid(sceneObject, $"child body {index} is not valid ({exception.Message})");
                }
            }

            return children;
        }

        private static List<ChildBodyDesc2D> ChildBodies2DOf(SceneFileObject sceneObject)
        {
            var children = new List<ChildBodyDesc2D>(sceneObject.ChildBodies2D.Count);
            for (int index = 0; index < sceneObject.ChildBodies2D.Count; index++)
            {
                SceneFileChildBody2D child = sceneObject.ChildBodies2D[index];
                if (child?.Body == null || child.Body.Kind == 0)
                {
                    throw Invalid(sceneObject, $"2D child body {index} has no body");
                }

                var localPosition = new Vector2(child.PositionX, child.PositionY);
                try
                {
                    children.Add(new ChildBodyDesc2D(SceneFileGeometry.ToDesc(child.Body, localPosition, child.Rotation), localPosition, child.Rotation));
                }
                catch (ArgumentException exception)
                {
                    throw Invalid(sceneObject, $"2D child body {index} is not valid ({exception.Message})");
                }
            }

            return children;
        }

        private static BehaviourSite SiteOf(SceneFileObject sceneObject, int behaviour, int children, int children2D)
        {
            int body = BodyOf(sceneObject, sceneObject.BehaviourBodies, behaviour, children, "BehaviourBodies");
            int body2D = BodyOf(sceneObject, sceneObject.BehaviourBodies2D, behaviour, children2D, "BehaviourBodies2D");
            if (body >= 0 && body2D >= 0)
            {
                throw Invalid(sceneObject, $"behaviour {behaviour} sits on both a child body and a 2D child body");
            }

            if (body >= 0)
            {
                return new BehaviourSite(ChildBody.Of(body));
            }

            return body2D >= 0 ? new BehaviourSite(ChildBody.Of2D(body2D)) : default;
        }

        private static int BodyOf(SceneFileObject sceneObject, List<int> bodies, int behaviour, int count, string field)
        {
            if (bodies.Count != sceneObject.BehaviourTypes.Count)
            {
                throw Invalid(sceneObject, $"{field} has {bodies.Count} entries for {sceneObject.BehaviourTypes.Count} behaviours");
            }

            int body = bodies[behaviour];
            if (body < -1 || body >= count)
            {
                throw Invalid(sceneObject, $"{field} names body {body} of {count} for behaviour {behaviour}");
            }

            return body;
        }

        private static InvalidDataException Invalid(SceneFileObject sceneObject, string what) =>
            new InvalidDataException($"scene object 0x{sceneObject.SceneObjectId:X16}: {what} in the scene file; export the scene again");
    }
}
