using System.Collections.Generic;
using System;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking;
using UnityEngine;

namespace Fomoxa.Unity
{
    [DisallowMultipleComponent]
    public sealed class NetworkObject : MonoBehaviour
    {
        [SerializeField] private uint prefabId;
        [SerializeField] private bool explicitPrefabId;
        [SerializeField] private ulong sceneObjectId;
        [SerializeField] private bool despawnWithOwner = true;
        [SerializeField] private NetworkVisibility visibility = NetworkVisibility.Rule;

        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private NetworkBehaviour[] behaviours = Array.Empty<NetworkBehaviour>();

        public uint ObjectId { get; private set; }

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

        internal ServerManager Server { get; private set; }

        public Fomoxa.Networking.Simulation.PhysicsBody Body => (Server?.Physics ?? Client?.Physics)?.BodyOf(this) ?? default;

        public Fomoxa.Networking.Simulation.PhysicsBody2D Body2D => (Server?.Physics ?? Client?.Physics)?.Body2DOf(this) ?? default;

        internal ClientManager Client { get; private set; }

        internal LinkedListNode<NetworkObject> SpawnOrderNode { get; set; }

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

            for (int index = 0; index < found.Length; index++)
            {
                found[index].Attach(this, (byte)index);
            }

            behaviours = found;
            Range = GetComponent<ObserverRange>();
        }

        internal bool HasInput { get; private set; }

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

        internal void Register(RpcMessageIds rpcIds, MessageChannels channels, StateProtocol stateProtocol, InputRules inputRules)
        {
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                behaviour.Register(rpcIds, channels, stateProtocol, inputRules);
                HasInput |= behaviour.InputSlot != null;
            }
        }

        internal void CaptureStates()
        {
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                if (behaviour.StateSlot != null)
                {
                    behaviour.PrepareState();
                    behaviour.StateSlot.Capture();
                }
            }
        }

        internal bool TryApplyStates(IReadOnlyList<ReadOnlyMemory<byte>> states, bool shared)
        {
            if (states.Count != behaviours.Length)
            {
                return false;
            }

            for (int index = 0; index < behaviours.Length; index++)
            {
                StateSlot slot = behaviours[index].StateSlot;
                if (slot == null || states[index].IsEmpty)
                {
                    if (slot != null || !states[index].IsEmpty)
                    {
                        return false;
                    }

                    continue;
                }

                try
                {
                    slot.Validate(states[index]);
                }
                catch (MessageDecodeException)
                {
                    return false;
                }
            }

            for (int index = 0; index < behaviours.Length; index++)
            {
                behaviours[index].StateSlot?.ApplyInitial(states[index], shared);
            }

            return true;
        }

        internal void AttachServer(ServerManager server, uint objectId)
        {
            Server = server;
            ObjectId = objectId;
        }

        internal void DetachServer()
        {
            Server = null;
            if (Client == null)
            {
                ObjectId = 0;
            }
        }

        internal void AttachClient(ClientManager client, uint objectId)
        {
            Client = client;
            ObjectId = objectId;
        }

        internal void DetachClient()
        {
            Client = null;
            if (Server == null)
            {
                ObjectId = 0;
            }
        }

        internal void StartServer()
        {
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                behaviour.OnStartServer();
            }
        }

        internal void StopServer()
        {
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                behaviour.OnStopServer();
            }
        }

        internal void StartClient()
        {
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                behaviour.OnStartClient();
            }
        }

        internal void StopClient()
        {
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                behaviour.OnStopClient();
            }
        }

        internal void ResetClientReceive()
        {
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                (behaviour as NetworkTransform)?.ResetReceive();
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

        internal void OwnerChangedServer(ulong previousOwnerId)
        {
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                behaviour.OnOwnerChangedServer(previousOwnerId);
            }
        }

        internal void OwnerChangedClient(ulong previousOwnerId)
        {
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                behaviour.OnOwnerChangedClient(previousOwnerId);
            }
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
    }
}
