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
        private bool ownsPhysicsSimulation;
        private PlayerLoopSystem.UpdateFunction frameEnd;

        public FomoxaRegistry Registry { get; set; } = FomoxaRegistry.Default;

        public ServerManager ServerManager { get; private set; }

        public ClientManager ClientManager { get; private set; }

        public TimeManager TimeManager { get; private set; }

        public TransportManager TransportManager { get; private set; }

        public PrefabRegistry Prefabs { get; } = new PrefabRegistry();

        public SceneRegistry Scenes { get; } = new SceneRegistry();

        internal bool CollectPrefabs => collectPrefabs;

        internal Func<IReadOnlyList<NetworkObject>> FindServerSceneObjects
        {
            get => serverScenes.FindSceneObjects;
            set => serverScenes.FindSceneObjects = value;
        }

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
            if (TimeManager != null)
            {
                return;
            }

            Schema schema = Registry?.Schema
                ?? throw new InvalidOperationException("no Fomoxa schema is registered; FomoxaAdapters.RegisterAll has not run");
            IMessageCodec<MessageBundle> bundleCodec = RequireCodec<MessageBundle>();
            IMessageCodec<ReliableAck> ackCodec = RequireCodec<ReliableAck>();
            IMessageCodec<PeerLeave> leaveCodec = RequireCodec<PeerLeave>();
            var objectProtocol = new ObjectProtocol(
                RequireCodec<LocalPeer>(),
                RequireCodec<ObjectSpawn>(),
                RequireCodec<ObjectSceneSpawn>(),
                RequireCodec<ObjectDespawn>(),
                RequireCodec<ObjectOwnerChange>(),
                Registry.Channels);
            var stateProtocol = new StateProtocol(RequireCodec<StateDelta>(), RequireCodec<StateResync>(), RequireCodec<AnimatorState>(), Registry.Channels);
            var transformProtocol = new TransformProtocol(RequireCodec<TransformUpdate>(), RequireCodec<TransformSettle>(), Registry.Channels);
            var sceneProtocol = new SceneProtocol(RequireCodec<SceneLoad>(), RequireCodec<SceneUnload>(), RequireCodec<SceneLoaded>(), Registry.Channels);
            var clockProtocol = new ClockProtocol(RequireCodec<TickPing>(), RequireCodec<TickPong>());
            var inputProtocol = new InputProtocol(RequireCodec<InputFrames>(), RequireCodec<ReconcileState>());
            var inputRules = new InputRules
            {
                Allowed = timingMode == TimingMode.Tick,
                Redundancy = inputRedundancy,
                History = predictionHistory,
                ReconcileInterval = reconcileInterval,
            };
            var clockSettings = new ClockSettings
            {
                PingInterval = TimeSpan.FromSeconds(clockPingIntervalSeconds),
                InputBuffer = inputBuffer,
                MaxSpeedAdjust = maxTickSpeedAdjust,
                ResetThreshold = clockResetTicks,
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

            var limits = new SessionLimits
            {
                MessageCapacity = messageCapacity,
                ByteCapacity = byteCapacity,
                ReliableWindow = reliableWindow,
            };
            TimeManager = new TimeManager(tickRate, maxTicksPerFrame, timingMode);
            TransportManager = new TransportManager(transport);
            var protocol = new SessionProtocol(
                new BundleFormat(bundleCodec, TransportManager.Factory.FrameBudget),
                ackCodec,
                leaveCodec,
                Registry.Channels);
            var sessionConfig = new SessionConfig
            {
                HandshakeTimeout = TimeSpan.FromSeconds(handshakeTimeoutSeconds),
                HeartbeatInterval = TimeSpan.FromSeconds(heartbeatIntervalSeconds),
                HeartbeatTimeout = TimeSpan.FromSeconds(heartbeatTimeoutSeconds),
            };
            serverScenes = new UnityServerSceneHost(Scenes);
            serverBackend = new UnityServerEntityBackend(Prefabs, serverScenes);
            ServerManager = new ServerManager(schema, limits, sessionConfig, protocol, objectProtocol, stateProtocol, transformProtocol, sceneProtocol, clockProtocol, inputProtocol, Registry.Rpcs, TransportManager.Factory, messageCapacity, serverBackend, serverScenes, UnityNetworkLog.Instance)
            {
                ObserverRule = observerRule != null ? observerRule : null,
                ObserverInterval = observerInterval,
                InputRules = inputRules,
            };
            ServerManager.Inputs.MaxInputLead = maxInputLead;
            ServerManager.Entities.OnUnspawning += EndHostShare;
            var reconnectPolicy = new ReconnectPolicy
            {
                MaxRetries = maxReconnectRetries,
                Interval = TimeSpan.FromSeconds(reconnectIntervalSeconds),
            };
            ClientManager = new ClientManager(schema, limits, sessionConfig, protocol, objectProtocol, stateProtocol, transformProtocol, sceneProtocol, clockProtocol, clockSettings, inputProtocol, TimeManager, Prefabs, Scenes, Registry.Rpcs, reconnectPolicy, TransportManager, ServerManager);
            ServerManager.Objects.TickRate = (ushort)TimeManager.TickRate;
            ServerManager.Objects.PhysicsBackend = physicsBackend;
            ClientManager.Objects.PhysicsBackend = physicsBackend;
            physicsWorlds = new PhysicsWorlds(physicsBackend);
            serverBackend.Physics = physicsWorlds;
            ClientManager.Physics = physicsWorlds;
            if (simulatePhysics)
            {
                PhysicsSimulationOwner.Acquire();
                ownsPhysicsSimulation = true;
            }

            ClientManager.SimulatesPhysics = ownsPhysicsSimulation;
            Application.quitting += StopConnections;
            ClientManager.InputRules = inputRules;
            TimeManager.Clock = ClientManager.Clock;
            TimeManager.FollowsClock = () => ServerManager.State != ServerState.Started && !ClientManager.ConnectedLocally;
            if (logSendDropped)
            {
                ServerManager.OnSendDropped += LogSendDropped;
                ClientManager.OnSendDropped += LogSendDropped;
            }

            if (logReceiveDropped)
            {
                ServerManager.OnReceiveDropped += LogReceiveDropped;
                ClientManager.OnReceiveDropped += LogReceiveDropped;
            }
        }

        internal void RunFrameStart(double unscaledDeltaSeconds, TimeSpan now)
        {
            int ticks = TimeManager.Advance(unscaledDeltaSeconds);
            if (TimeManager.Mode == TimingMode.Variable)
            {
                Receive(now);
            }

            for (int index = 0; index < ticks; index++)
            {
                TimeManager.RaisePreTick(now);
                if (index == 0 && TimeManager.Mode == TimingMode.Tick)
                {
                    Receive(now);
                }

                if (ownsPhysicsSimulation)
                {
                    ClientManager.PlaceProxies();
                }

                if (TimeManager.Mode == TimingMode.Tick)
                {
                    ClientManager.Predict(TimeManager.PredictionTick);
                    ServerManager.ApplyInputs(TimeManager.Tick, ClientManager.ConnectedLocally ? ClientManager.Objects.LocalPeerId : 0);
                }

                if (ownsPhysicsSimulation)
                {
                    physicsWorlds.StepWorlds((float)TimeManager.TickDelta);
                    physicsWorlds.QueryContacts(TimeManager.PredictionTick, TimeManager.Mode == TimingMode.Tick && ClientManager.RecordsContacts, ClientManager.InputRules.History);
                }

                if (TimeManager.Mode == TimingMode.Tick)
                {
                    ClientManager.CapturePredicted(TimeManager.PredictionTick);
                    ServerManager.SendReconcileStates(TimeManager.Tick);
                }

                if (ownsPhysicsSimulation)
                {
                    physicsWorlds.PublishContacts();
                }

                TimeManager.RaiseTick();
                TimeManager.RaisePostTick();
                ServerManager.RebuildObserversRound();
                ServerManager.SyncStates();
                ServerManager.SyncTransforms(TimeManager.Tick);
                if (TimeManager.Mode == TimingMode.Tick)
                {
                    Send();
                }
            }
        }

        internal void RunFrameEnd()
        {
            if (TimeManager.Mode == TimingMode.Variable)
            {
                Send();
            }
        }

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            if (TimeManager == null)
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
            if (TimeManager == null)
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
            if (ownsPhysicsSimulation)
            {
                ownsPhysicsSimulation = false;
                ClientManager.SimulatesPhysics = false;
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

        private void Receive(TimeSpan now)
        {
            ServerManager.Clock.Tick = TimeManager.Tick;
            ServerManager.Tick(now);
            ClientManager.Tick(now);
            ClientManager.UpdateClock(now);
        }

        private void Send()
        {
            ServerManager.Flush();
            ClientManager.Flush();
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

        private IMessageCodec<T> RequireCodec<T>() =>
            Registry.Codec<T>()
            ?? throw new InvalidOperationException($"no {typeof(T).Name} codec is registered; FomoxaAdapters.RegisterAll has not run");

        private static void EndHostShare(EntityRecord record)
        {
            var networkObject = (NetworkObject)record.Representation;
            networkObject.Client?.EndShared(networkObject);
            networkObject.RestoreRenderers();
        }

        private static void LogSendDropped(SendDroppedArgs args)
        {
            Debug.LogWarning($"Fomoxa dropped message 0x{args.MessageId:X8} ({args.PayloadLength} bytes) to peer {args.PeerId}: {args.Reason}");
        }

        private static void LogReceiveDropped(ReceiveDroppedArgs args)
        {
            Debug.LogWarning($"Fomoxa dropped {args.FrameLength} received bytes from peer {args.PeerId}: a message bundle, or a model in it, could not be read");
        }
    }
}
