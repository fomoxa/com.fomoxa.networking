using System.Collections.Generic;
using System;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking;
using UnityEngine;

namespace Fomoxa.Unity
{
    [DisallowMultipleComponent]
    public sealed class NetworkObject : MonoBehaviour, IBehaviourLink, INetworkEntity
    {
        [SerializeField] private uint prefabId;
        [SerializeField] private bool explicitPrefabId;
        [SerializeField] private ulong sceneObjectId;
        [SerializeField] private bool despawnWithOwner = true;
        [SerializeField] private NetworkVisibility visibility = NetworkVisibility.Rule;

        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private NetworkBehaviour[] behaviours = Array.Empty<NetworkBehaviour>();
        private EntityBehaviour[] entityBehaviours = Array.Empty<EntityBehaviour>();
        private EntityRecord record;

        public uint ObjectId => record != null ? record.ObjectId : 0;

        public uint PrefabId => prefabId;

        public ulong SceneObjectId => sceneObjectId;

        public ulong OwnerId
        {
            get
            {
                if (Server != null && Server.Objects.TryGet(ObjectId, out ObjectRow row))
                {
                    return row.OwnerId;
                }

                if (Client != null && Client.Objects.TryGet(ObjectId, out row))
                {
                    return row.OwnerId;
                }

                return 0;
            }
        }

        public bool IsOwner => Client != null && Client.Objects.IsOwner(ObjectId);

        public bool IsSpawned => Server != null || Client != null;

        public bool DespawnWithOwner
        {
            get => despawnWithOwner;
            set => despawnWithOwner = value;
        }

        public NetworkVisibility Visibility
        {
            get => visibility;
            set => visibility = value;
        }

        public IReadOnlyList<NetworkBehaviour> Behaviours => behaviours;

        internal bool ExplicitPrefabId => explicitPrefabId;

        internal ServerManager Server => record?.Server?.Owner as ServerManager;

        public Fomoxa.Networking.Simulation.PhysicsBody Body => PhysicsWorlds?.BodyOf(this) ?? default;

        public Fomoxa.Networking.Simulation.PhysicsBody2D Body2D => PhysicsWorlds?.Body2DOf(this) ?? default;

        internal ClientManager Client => record?.Client?.Owner as ClientManager;

        private PhysicsWorlds PhysicsWorlds => (Server?.EntityBackend as UnityServerEntityBackend)?.Physics ?? Client?.Physics;

        internal uint Fingerprint { get; set; }

        internal bool HiddenOnHost { get; private set; }

        internal ObserverRange Range { get; private set; }

        internal void SetPrefabId(uint value) => prefabId = value;

        internal void SetSceneObjectId(ulong value) => sceneObjectId = value;

        internal void CollectBehaviours()
        {
            NetworkBehaviour[] found = GetComponentsInChildren<NetworkBehaviour>(true);
            if (found.Length > PrefabHash.MaxBehaviours)
            {
                throw new InvalidOperationException($"{name}: {found.Length} NetworkBehaviours exceed the limit of {PrefabHash.MaxBehaviours}");
            }

            var cores = new EntityBehaviour[found.Length];
            for (int index = 0; index < found.Length; index++)
            {
                found[index].Attach(this, (byte)index);
                cores[index] = found[index].Core;
            }

            behaviours = found;
            entityBehaviours = cores;
            Range = GetComponent<ObserverRange>();
        }

        internal bool IsPredicting
        {
            get
            {
                foreach (NetworkBehaviour behaviour in behaviours)
                {
                    if (behaviour.IsPredicting)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal void HideOnHost()
        {
            if (HiddenOnHost)
            {
                return;
            }

            HiddenOnHost = true;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    hiddenRenderers.Add(renderer);
                }
            }

            NotifyHostVisibility(false);
        }

        internal void ShowOnHost()
        {
            if (!RestoreRenderers())
            {
                return;
            }

            NotifyHostVisibility(true);
        }

        internal bool RestoreRenderers()
        {
            if (!HiddenOnHost)
            {
                return false;
            }

            HiddenOnHost = false;
            foreach (Renderer renderer in hiddenRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }

            hiddenRenderers.Clear();
            return true;
        }

        internal void DestroyGameObject()
        {
            if (Application.isPlaying)
            {
                Destroy(gameObject);
                return;
            }

            DestroyImmediate(gameObject);
        }

        internal void HandleDestroy()
        {
            if (Server != null)
            {
                Server.DespawnDestroyed(this);
                return;
            }

            Client?.ForgetDestroyed(this);
        }

        private void NotifyHostVisibility(bool visible)
        {
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                try
                {
                    behaviour.HostVisibility(visible);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void OnDestroy()
        {
            HandleDestroy();
        }

        bool IBehaviourLink.SpawnedOnServer => Server != null;

        bool IBehaviourLink.SpawnedOnClient => Client != null;

        bool IBehaviourLink.IsReplaying => Client != null && Client.IsReplaying;

        RpcMessageIds IBehaviourLink.RpcIds(bool server) => server ? Server.RpcIds : Client.RpcIds;

        SendResult IBehaviourLink.SendToServer(uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            Client.SendToObject(messageId, ObjectId, behaviourIndex, body);

        int IBehaviourLink.SendToObservers(uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            Server.BroadcastToObject(messageId, ObjectId, behaviourIndex, body);

        SendResult IBehaviourLink.SendToObserver(ulong peerId, uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            Server.SendToObserver(peerId, messageId, ObjectId, behaviourIndex, body);

        IReadOnlyList<EntityBehaviour> INetworkEntity.EntityBehaviours => entityBehaviours;

        EntityRecord INetworkEntity.Record => record;

        System.Numerics.Vector3 INetworkEntity.ReadWorldPosition() => transform.position.ToNumerics();

        void INetworkEntity.ReadRootPose(out System.Numerics.Vector3 worldPosition, out System.Numerics.Quaternion worldRotation, out System.Numerics.Vector3 localScale)
        {
            Transform root = transform;
            worldPosition = root.position.ToNumerics();
            worldRotation = root.rotation.ToNumerics();
            localScale = root.localScale.ToNumerics();
        }

        void INetworkEntity.Bind(EntityRecord bound) => record = bound;

        void INetworkEntity.Unbind(EntityRecord bound)
        {
            if (record == bound)
            {
                record = null;
            }
        }
    }
}
