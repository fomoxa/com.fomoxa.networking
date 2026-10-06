using System.Collections.Generic;
using System;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine;

namespace Fomoxa.Unity
{
    [DisallowMultipleComponent]
    public sealed class NetworkManager : MonoBehaviour
    {
        [SerializeField] private NetworkTransport transport;
        [SerializeField] private int tickRate = 30;
        [SerializeField] private TimingMode timingMode = TimingMode.Tick;
        [SerializeField] private int maxTicksPerFrame = 3;
        [SerializeField] private int messageCapacity = 1024;
        [SerializeField] private int byteCapacity = 1024 * 1024;
        [SerializeField] private int reliableWindow = 1024;
        [SerializeField] private bool logSendDropped = true;
        [SerializeField] private bool logReceiveDropped = true;
        [SerializeField] private int maxReconnectRetries = -1;
        [SerializeField] private float reconnectIntervalSeconds = 0.5f;
        [SerializeField] private float handshakeTimeoutSeconds = 5f;
        [SerializeField] private float heartbeatIntervalSeconds = 5f;
        [SerializeField] private float heartbeatTimeoutSeconds = 15f;
        [SerializeField] private bool collectPrefabs = true;
        [SerializeField] private NetworkPrefabList collectedPrefabs;
        [SerializeField] private NetworkSceneList collectedScenes;
        [SerializeField] private List<NetworkObject> prefabs = new List<NetworkObject>();
        [SerializeField] private ObserverRule observerRule;
        [SerializeField] private int observerInterval = 15;
        [SerializeField] private float clockPingIntervalSeconds = 1f;
        [SerializeField] private int inputBuffer = 2;
        [SerializeField] private float maxTickSpeedAdjust = 0.1f;
        [SerializeField] private int clockResetTicks = 10;
        [SerializeField] private int inputRedundancy = 3;
        [SerializeField] private int maxInputLead = 30;
        [SerializeField] private int predictionHistory = 64;
        [SerializeField] private int reconcileInterval = 1;
        [SerializeField] private PhysicsBackend physicsBackend = PhysicsBackend.Rigidbody;
        [SerializeField] private bool simulatePhysics;

        private PlayerLoopSystem.UpdateFunction frameStart;
        private PhysicsWorlds physicsWorlds;
        private UnityServerSceneHost serverScenes;
        private UnityServerEntityBackend serverBackend;
        private UnityClientEntityBackend clientBackend;
        private UnityPredictionBackend predictionBackend;
        private NetworkRuntime runtime;
        private PlayerLoopSystem.UpdateFunction frameEnd;

        public FomoxaRegistry Registry { get; set; } = FomoxaRegistry.Default;

        public ServerManager ServerManager => runtime?.ServerManager;

        public ClientManager ClientManager => runtime?.ClientManager;

        public TimeManager TimeManager => runtime?.TimeManager;

        public TransportManager TransportManager { get; private set; }

        public PrefabRegistry Prefabs { get; } = new PrefabRegistry();

        public SceneRegistry Scenes { get; } = new SceneRegistry();

        internal bool CollectPrefabs => collectPrefabs;

        internal Func<IReadOnlyList<NetworkObject>> FindServerSceneObjects
        {
            get => serverScenes.FindSceneObjects;
            set => serverScenes.FindSceneObjects = value;
        }

        internal Func<IReadOnlyList<NetworkObject>> FindClientSceneObjects
        {
            get => clientBackend.FindSceneObjects;
            set => clientBackend.FindSceneObjects = value;
        }

        internal PhysicsWorlds Physics => physicsWorlds;

        internal NetworkPrefabList CollectedPrefabs
        {
            get => collectedPrefabs;
            set => collectedPrefabs = value;
        }

        internal NetworkSceneList CollectedScenes
        {
            get => collectedScenes;
            set => collectedScenes = value;
        }

        internal void Initialize()
        {
            if (runtime != null)
            {
                return;
            }

            var settings = new NetworkSettings
            {
                TickRate = tickRate,
                MaxTicksPerFrame = maxTicksPerFrame,
                TimingMode = timingMode,
                MessageCapacity = messageCapacity,
                ByteCapacity = byteCapacity,
                ReliableWindow = reliableWindow,
                LogSendDropped = logSendDropped,
                LogReceiveDropped = logReceiveDropped,
                MaxReconnectRetries = maxReconnectRetries,
                ReconnectInterval = TimeSpan.FromSeconds(reconnectIntervalSeconds),
                HandshakeTimeout = TimeSpan.FromSeconds(handshakeTimeoutSeconds),
                HeartbeatInterval = TimeSpan.FromSeconds(heartbeatIntervalSeconds),
                HeartbeatTimeout = TimeSpan.FromSeconds(heartbeatTimeoutSeconds),
                ObserverInterval = observerInterval,
                ClockPingInterval = TimeSpan.FromSeconds(clockPingIntervalSeconds),
                InputBuffer = inputBuffer,
                MaxTickSpeedAdjust = maxTickSpeedAdjust,
                ClockResetTicks = clockResetTicks,
                InputRedundancy = inputRedundancy,
                MaxInputLead = maxInputLead,
                PredictionHistory = predictionHistory,
                ReconcileInterval = reconcileInterval,
                PhysicsBackend = physicsBackend,
            };
            if (collectPrefabs && collectedPrefabs != null)
            {
                RegisterPrefabs(collectedPrefabs.Prefabs);
            }

            RegisterPrefabs(prefabs);
            if (collectedScenes != null)
            {
                Scenes.RegisterBuildScenes(collectedScenes);
            }

            if (transport == null)
            {
                transport = gameObject.AddComponent<UdpNetworkTransport>();
            }

            TransportManager = new TransportManager(transport);
            serverScenes = new UnityServerSceneHost(Scenes);
            serverBackend = new UnityServerEntityBackend(Prefabs, serverScenes);
            clientBackend = new UnityClientEntityBackend(Prefabs);
            predictionBackend = new UnityPredictionBackend();
            var backends = new NetworkBackends(serverBackend, serverScenes, clientBackend, new UnityClientSceneHost(Scenes, clientBackend), predictionBackend);
            runtime = new NetworkRuntime(Registry, settings, TransportManager.Factory, backends, () => MonotonicClock.Now, UnityNetworkLog.Instance);
            ServerManager.ObserverRule = observerRule != null ? observerRule : null;
            ServerManager.Entities.OnUnspawning += EndHostShare;
            physicsWorlds = new PhysicsWorlds(physicsBackend);
            serverBackend.Physics = physicsWorlds;
            predictionBackend.Physics = physicsWorlds;
            if (simulatePhysics)
            {
                PhysicsSimulationOwner.Acquire();
                runtime.Physics = physicsWorlds;
            }

            Application.quitting += StopConnections;
        }

        internal void RunFrameStart(double unscaledDeltaSeconds, TimeSpan now) => runtime.BeginFrame(now, unscaledDeltaSeconds);

        internal void RunFrameEnd() => runtime.EndFrame();

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            if (runtime == null)
            {
                return;
            }

            frameStart = OnFrameStart;
            frameEnd = OnFrameEnd;
            NetworkPlayerLoop.InsertFirst(typeof(EarlyUpdate), frameStart);
            NetworkPlayerLoop.InsertLast(typeof(PostLateUpdate), frameEnd);
        }

        private void OnDisable()
        {
            if (frameStart == null)
            {
                return;
            }

            NetworkPlayerLoop.Remove(frameStart);
            NetworkPlayerLoop.Remove(frameEnd);
            frameStart = null;
            frameEnd = null;
        }

        private void OnDestroy()
        {
            if (runtime == null)
            {
                return;
            }

            Application.quitting -= StopConnections;
            StopConnections();
            ReleasePhysicsSimulation();
        }

        private void StopConnections()
        {
            try
            {
                ClientManager.StopConnection();
            }
            finally
            {
                ServerManager.StopConnection();
            }
        }

        internal void ReleasePhysicsSimulation()
        {
            if (runtime?.Physics != null)
            {
                runtime.Physics = null;
                ClientManager.EndProxies();
                physicsWorlds.ReleaseStepping();
                PhysicsSimulationOwner.Release();
            }
        }

        private void OnFrameStart()
        {
            RunFrameStart(Time.unscaledDeltaTime, MonotonicClock.Now);
        }

        private void OnFrameEnd()
        {
            RunFrameEnd();
        }

        private void RegisterPrefabs(IReadOnlyList<NetworkObject> list)
        {
            foreach (NetworkObject prefab in list)
            {
                if (prefab != null)
                {
                    Prefabs.Register(prefab);
                }
            }
        }

        private static void EndHostShare(EntityRecord record)
        {
            var networkObject = (NetworkObject)record.Representation;
            networkObject.Client?.EndShared(networkObject);
            networkObject.RestoreRenderers();
        }
    }
}
