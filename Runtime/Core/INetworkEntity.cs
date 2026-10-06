using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Networking
{
    public interface INetworkEntity
    {
        uint PrefabId { get; }

        ulong SceneObjectId { get; }

        bool DespawnWithOwner { get; }

        NetworkVisibility Visibility { get; }

        IReadOnlyList<EntityBehaviour> EntityBehaviours { get; }

        EntityRecord Record { get; }

        Vector3 ReadWorldPosition();

        void ReadRootPose(out Vector3 worldPosition, out Quaternion worldRotation, out Vector3 localScale);

        void Bind(EntityRecord record);

        void Unbind(EntityRecord record);
    }
}
