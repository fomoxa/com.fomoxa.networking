using System.Collections.Generic;

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

        void Bind(EntityRecord record);

        void Unbind(EntityRecord record);
    }
}
