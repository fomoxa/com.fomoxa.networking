using System;
using System.Collections.Generic;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking
{
    public sealed class NetworkRuntime
    {
        public const string Version = "0.1.0";

        private readonly FomoxaRegistry registry;
        private readonly NetworkLog log;
        private readonly PhysicsBackend physicsBackend;
        private readonly List<IPhysicsSimulation> stepping = new List<IPhysicsSimulation>();
        private readonly List<IContactTracker> queried = new List<IContactTracker>();
        private IPhysicsWorlds physics;
        private bool open;
        private bool executing;

        public NetworkRuntime(FomoxaRegistry registry, NetworkSettings settings, ITransportFactory transport, NetworkBackends backends, Func<TimeSpan> clock, NetworkLog log)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.log = log;
            physicsBackend = settings.PhysicsBackend;
            Schema schema = registry.Schema
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
                registry.Channels);
            var stateProtocol = new StateProtocol(RequireCodec<StateDelta>(), RequireCodec<StateResync>(), RequireCodec<AnimatorState>(), registry.Channels);
            var transformProtocol = new TransformProtocol(RequireCodec<TransformUpdate>(), RequireCodec<TransformSettle>(), registry.Channels);
            var sceneProtocol = new SceneProtocol(RequireCodec<SceneLoad>(), RequireCodec<SceneUnload>(), RequireCodec<SceneLoaded>(), registry.Channels);
            var clockProtocol = new ClockProtocol(RequireCodec<TickPing>(), RequireCodec<TickPong>());
            var inputProtocol = new InputProtocol(RequireCodec<InputFrames>(), RequireCodec<ReconcileState>());
            var inputRules = new InputRules
            {
                Allowed = settings.TimingMode == TimingMode.Tick,
                Redundancy = settings.InputRedundancy,
                History = settings.PredictionHistory,
                ReconcileInterval = settings.ReconcileInterval,
            };
            var clockSettings = new ClockSettings
            {
                PingInterval = settings.ClockPingInterval,
                InputBuffer = settings.InputBuffer,
                MaxSpeedAdjust = settings.MaxTickSpeedAdjust,
                ResetThreshold = settings.ClockResetTicks,
            };
            var limits = new SessionLimits
            {
                MessageCapacity = settings.MessageCapacity,
                ByteCapacity = settings.ByteCapacity,
                ReliableWindow = settings.ReliableWindow,
            };
            TimeManager = new TimeManager(settings.TickRate, settings.MaxTicksPerFrame, settings.TimingMode);
            var protocol = new SessionProtocol(
                new BundleFormat(bundleCodec, transport.FrameBudget),
                ackCodec,
                leaveCodec,
                registry.Channels);
            var sessionConfig = new SessionConfig
            {
                HandshakeTimeout = settings.HandshakeTimeout,
                HeartbeatInterval = settings.HeartbeatInterval,
                HeartbeatTimeout = settings.HeartbeatTimeout,
            };
            ServerManager = new ServerManager(schema, limits, sessionConfig, protocol, objectProtocol, stateProtocol, transformProtocol, sceneProtocol, clockProtocol, inputProtocol, registry.Rpcs, transport, settings.MessageCapacity, backends.ServerEntities, backends.ServerScenes, log)
            {
                ObserverInterval = settings.ObserverInterval,
                InputRules = inputRules,
            };
            ServerManager.Inputs.MaxInputLead = settings.MaxInputLead;
            var reconnectPolicy = new ReconnectPolicy
            {
                MaxRetries = settings.MaxReconnectRetries,
                Interval = settings.ReconnectInterval,
            };
            ClientManager = new ClientManager(schema, limits, sessionConfig, protocol, objectProtocol, stateProtocol, transformProtocol, sceneProtocol, clockProtocol, clockSettings, inputProtocol, TimeManager, registry.Rpcs, reconnectPolicy, transport, ServerManager, backends.ClientEntities, backends.ClientScenes, backends.Prediction, clock, log);
            ServerManager.Objects.TickRate = (ushort)TimeManager.TickRate;
            ServerManager.Objects.PhysicsBackend = settings.PhysicsBackend;
            ClientManager.Objects.PhysicsBackend = settings.PhysicsBackend;
            ClientManager.InputRules = inputRules;
            TimeManager.Clock = ClientManager.Clock;
            TimeManager.FollowsClock = () => ServerManager.State != ServerState.Started && !ClientManager.ConnectedLocally;
            if (settings.LogSendDropped)
            {
                ServerManager.OnSendDropped += LogSendDropped;
                ClientManager.OnSendDropped += LogSendDropped;
            }

            if (settings.LogReceiveDropped)
            {
                ServerManager.OnReceiveDropped += LogReceiveDropped;
                ClientManager.OnReceiveDropped += LogReceiveDropped;
            }
        }

        public ServerManager ServerManager { get; }

        public ClientManager ClientManager { get; }

        public TimeManager TimeManager { get; }

        public IPhysicsWorlds Physics
        {
            get => physics;
            set
            {
                if (executing)
                {
                    throw new InvalidOperationException("Physics was attached from inside a frame of the same runtime");
                }

                if (value != null && value.Backend != physicsBackend)
                {
                    throw new ArgumentException($"the physics backend {value.Backend} differs from NetworkSettings.PhysicsBackend {physicsBackend}", nameof(value));
                }

                physics = value;
                ClientManager.Prediction.Physics = value;
            }
        }

        public void BeginFrame(TimeSpan now, double elapsedSeconds)
        {
            if (executing)
            {
                throw new InvalidOperationException("BeginFrame was called from inside a frame of the same runtime");
            }

            if (open)
            {
                throw new InvalidOperationException("BeginFrame was called before EndFrame closed the previous frame");
            }

            open = true;
            executing = true;
            try
            {
                RunFrame(now, elapsedSeconds);
            }
            finally
            {
                executing = false;
            }
        }

        public void EndFrame()
        {
            if (executing)
            {
                throw new InvalidOperationException("EndFrame was called from inside a frame of the same runtime");
            }

            if (!open)
            {
                throw new InvalidOperationException("EndFrame was called without a BeginFrame");
            }

            executing = true;
            try
            {
                if (TimeManager.Mode == TimingMode.Variable)
                {
                    Send();
                }
            }
            finally
            {
                open = false;
                executing = false;
            }
        }

        private void RunFrame(TimeSpan now, double elapsedSeconds)
        {
            int ticks = TimeManager.Advance(elapsedSeconds);
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

                IPhysicsWorlds worlds = physics;
                if (worlds != null)
                {
                    ClientManager.PlaceProxies();
                }

                if (TimeManager.Mode == TimingMode.Tick)
                {
                    ClientManager.Predict(TimeManager.PredictionTick);
                    ServerManager.ApplyInputs(TimeManager.Tick, ClientManager.ConnectedLocally ? ClientManager.Objects.LocalPeerId : 0);
                }

                if (worlds != null)
                {
                    StepWorlds(worlds, (float)TimeManager.TickDelta, TimeManager.PredictionTick, TimeManager.Mode == TimingMode.Tick && ClientManager.RecordsContacts, ClientManager.InputRules.History);
                }

                if (TimeManager.Mode == TimingMode.Tick)
                {
                    ClientManager.CapturePredicted(TimeManager.PredictionTick);
                    ServerManager.SendReconcileStates(TimeManager.Tick);
                }

                if (worlds != null)
                {
                    PublishContacts();
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

        private void StepWorlds(IPhysicsWorlds worlds, float seconds, uint tick, bool record, int capacity)
        {
            stepping.Clear();
            queried.Clear();
            worlds.WorldsToStep(stepping);
            foreach (IPhysicsSimulation world in stepping)
            {
                world.Step(seconds);
            }

            foreach (IPhysicsSimulation world in stepping)
            {
                IContactTracker tracker = worlds.TrackerOf(world);
                if (tracker == null)
                {
                    continue;
                }

                queried.Add(tracker);
                tracker.Query();
                if (record)
                {
                    tracker.Record(tick, capacity);
                }
            }

            stepping.Clear();
        }

        private void PublishContacts()
        {
            foreach (IContactTracker tracker in queried)
            {
                tracker.Publish();
            }

            queried.Clear();
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

        private IMessageCodec<T> RequireCodec<T>() =>
            registry.Codec<T>()
            ?? throw new InvalidOperationException($"no {typeof(T).Name} codec is registered; FomoxaAdapters.RegisterAll has not run");

        private void LogSendDropped(SendDroppedArgs args)
        {
            log.Warning($"Fomoxa dropped message 0x{args.MessageId:X8} ({args.PayloadLength} bytes) to peer {args.PeerId}: {args.Reason}");
        }

        private void LogReceiveDropped(ReceiveDroppedArgs args)
        {
            log.Warning($"Fomoxa dropped {args.FrameLength} received bytes from peer {args.PeerId}: a message bundle, or a model in it, could not be read");
        }
    }
}
