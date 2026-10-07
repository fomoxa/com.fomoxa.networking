using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Standalone
{
    public class StandaloneEntity : INetworkEntity
    {
        private readonly EntityBehaviour[] behaviours;
        private Vector3 position;
        private Quaternion rotation = Quaternion.Identity;
        private Vector3 scale = Vector3.One;
        private BodyDesc? fileBody;
        private BodyDesc2D? fileBody2D;
        private float? fileAngle;
        private bool attached;

        public StandaloneEntity(uint prefabId, IReadOnlyList<EntityBehaviour> behaviours)
            : this(prefabId, 0, 0, behaviours)
        {
        }

        internal StandaloneEntity(uint prefabId, ulong sceneObjectId, uint sceneFingerprint, IReadOnlyList<EntityBehaviour> behaviours)
        {
            if (behaviours == null)
            {
                throw new ArgumentNullException(nameof(behaviours));
            }

            PrefabId = prefabId;
            SceneObjectId = sceneObjectId;
            SceneFingerprint = sceneFingerprint;
            this.behaviours = new EntityBehaviour[behaviours.Count];
            for (int index = 0; index < behaviours.Count; index++)
            {
                this.behaviours[index] = behaviours[index];
            }

            EntityBehaviour.Attach(this);
        }

        public uint PrefabId { get; }

        public ulong SceneObjectId { get; }

        public uint SceneId { get; set; }

        public bool DespawnWithOwner { get; set; } = true;

        public NetworkVisibility Visibility { get; set; } = NetworkVisibility.Rule;

        public IReadOnlyList<EntityBehaviour> EntityBehaviours => behaviours;

        public EntityRecord Record { get; private set; }

        public PhysicsBody Body { get; private set; }

        public PhysicsBody2D Body2D { get; private set; }

        public virtual Vector3 Position
        {
            get
            {
                if (Body.IsValid)
                {
                    return Body.Position;
                }

                if (Body2D.IsValid)
                {
                    Vector2 planar = Body2D.Position;
                    return new Vector3(planar.X, planar.Y, position.Z);
                }

                return position;
            }

            set
            {
                position = value;
                if (Body.IsValid)
                {
                    PhysicsBody body = Body;
                    body.Position = value;
                }
                else if (Body2D.IsValid)
                {
                    PhysicsBody2D body = Body2D;
                    body.Position = new Vector2(value.X, value.Y);
                }
            }
        }

        public virtual Quaternion Rotation
        {
            get
            {
                if (Body.IsValid)
                {
                    return Body.Rotation;
                }

                if (Body2D.IsValid)
                {
                    return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Body2D.Rotation);
                }

                return rotation;
            }

            set
            {
                rotation = value;
                fileAngle = null;
                if (Body.IsValid)
                {
                    PhysicsBody body = Body;
                    body.Rotation = value;
                }
                else if (Body2D.IsValid)
                {
                    PhysicsBody2D body = Body2D;
                    body.Rotation = AngleOf(value);
                }
            }
        }

        public virtual Vector3 Scale
        {
            get => scale;
            set => scale = value;
        }

        internal uint SceneFingerprint { get; }

        public virtual bool TryGetBody(out BodyDesc body)
        {
            body = fileBody.GetValueOrDefault();
            return fileBody.HasValue;
        }

        public virtual bool TryGetBody2D(out BodyDesc2D body)
        {
            body = fileBody2D.GetValueOrDefault();
            return fileBody2D.HasValue;
        }

        internal void SetFileBodies(BodyDesc? body, BodyDesc2D? body2D)
        {
            fileBody = body;
            fileBody2D = body2D;
            fileAngle = body2D?.Rotation;
        }

        internal void AttachBodies(IPhysicsScenes physics)
        {
            Vector3 at = Position;
            Quaternion facing = Rotation;
            if (TryGetBody(out BodyDesc body))
            {
                Body = physics.AddBody(this, SceneId, new BodyDesc(body.Kind, body.Colliders, at, facing, body.Mass));
            }

            if (TryGetBody2D(out BodyDesc2D body2D))
            {
                Body2D = physics.AddBody2D(this, SceneId, new BodyDesc2D(body2D.Kind, body2D.Colliders, new Vector2(at.X, at.Y), fileAngle ?? AngleOf(facing), body2D.Mass));
            }

            attached = true;
        }

        internal void DetachBodies(IPhysicsScenes physics)
        {
            if (!attached)
            {
                return;
            }

            position = Position;
            rotation = Rotation;
            physics.RemoveBodies(this);
            Body = default;
            Body2D = default;
            attached = false;
        }

        private static float AngleOf(Quaternion rotation) => 2f * MathF.Atan2(rotation.Z, rotation.W);

        Vector3 INetworkEntity.ReadWorldPosition() => Position;

        void INetworkEntity.ReadRootPose(out Vector3 worldPosition, out Quaternion worldRotation, out Vector3 localScale)
        {
            worldPosition = Position;
            worldRotation = Rotation;
            localScale = Scale;
        }

        void INetworkEntity.Bind(EntityRecord record) => Record = record;

        void INetworkEntity.Unbind(EntityRecord record)
        {
            if (Record == record)
            {
                Record = null;
            }
        }
    }
}
