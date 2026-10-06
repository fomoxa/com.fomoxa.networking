using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Networking.Standalone
{
    public class StandaloneEntity : INetworkEntity
    {
        private readonly EntityBehaviour[] behaviours;
        private Vector3 position;
        private Quaternion rotation = Quaternion.Identity;
        private Vector3 scale = Vector3.One;

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

        public virtual Vector3 Position
        {
            get => position;
            set => position = value;
        }

        public virtual Quaternion Rotation
        {
            get => rotation;
            set => rotation = value;
        }

        public virtual Vector3 Scale
        {
            get => scale;
            set => scale = value;
        }

        internal uint SceneFingerprint { get; }

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
