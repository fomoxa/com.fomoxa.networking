using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Standalone.Tests
{
    public sealed class StandaloneBackendTest
    {
        private const uint PlayerPrefabId = 0x0000_0501;
        private const uint PlayerFingerprint = 0x0000_F1A7;
        private const uint ArenaSceneId = 0x00A1_E7A0;
        private const uint DoorFingerprint = 0x0000_D002;
        private const string CounterType = "Game.Counter";
        private const double FrameSeconds = 1.0 / 30;

        private FomoxaRegistry registry;
        private MemorySceneFiles files;
        private TimeSpan now;
        private NetworkRuntime server;
        private NetworkRuntime client;
        private StandalonePrefabs serverPrefabs;
        private StandalonePrefabs clientPrefabs;
        private StandaloneBehaviours behaviours;
        private List<ObjectMismatchArgs> mismatches;
        private List<Exception> logged;

        [SetUp]
        public void SetUp()
        {
            registry = TestObjects.Registry(CountCodec.Schemas());
            foreach (CountCodec codec in CountCodec.All)
            {
                registry.Channels.Set(codec.MessageId, Channel.ReliableOrdered);
            }

            files = new MemorySceneFiles();
            serverPrefabs = new StandalonePrefabs();
            clientPrefabs = new StandalonePrefabs();
            behaviours = new StandaloneBehaviours();
            behaviours.Register(CounterType, () => new CounterBehaviour());
            serverPrefabs.Register(PlayerPrefabId, PlayerFingerprint, () => new StandaloneEntity(PlayerPrefabId, new[] { new CounterBehaviour() }));
            mismatches = new List<ObjectMismatchArgs>();
            logged = new List<Exception>();
        }

        [TearDown]
        public void TearDown()
        {
            Assert.IsEmpty(logged);
        }

        [Test]
        public void APrefabSpawnedOnTheServerReachesTheClientWithItsPose()
        {
            clientPrefabs.Register(PlayerPrefabId, PlayerFingerprint, () => new StandaloneEntity(PlayerPrefabId, new[] { new CounterBehaviour() }));
            Connect();
            var player = new StandaloneEntity(PlayerPrefabId, new[] { new CounterBehaviour() })
            {
                Position = new Vector3(1, 2, 3),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f),
                Scale = new Vector3(2, 2, 2),
            };

            server.ServerManager.Spawn(player);
            Run(5);

            var onClient = (StandaloneEntity)client.ClientManager.Spawned[player.Record.ObjectId];
            Assert.AreEqual(PlayerPrefabId, onClient.PrefabId);
            Assert.AreEqual(player.Position, onClient.Position);
            Assert.AreEqual(player.Rotation, onClient.Rotation);
            Assert.AreEqual(player.Scale, onClient.Scale);
            Assert.IsInstanceOf<CounterBehaviour>(onClient.EntityBehaviours.Single());
            Assert.AreEqual(0, onClient.EntityBehaviours[0].BehaviourIndex);
        }

        [Test]
        public void APrefabWithAnotherFingerprintOnTheClientIsIncompatible()
        {
            clientPrefabs.Register(PlayerPrefabId, PlayerFingerprint + 1, () => new StandaloneEntity(PlayerPrefabId, new[] { new CounterBehaviour() }));
            Connect();
            var player = new StandaloneEntity(PlayerPrefabId, new[] { new CounterBehaviour() });

            server.ServerManager.Spawn(player);
            Run(5);

            Assert.AreEqual(0, client.ClientManager.Spawned.Count);
            Assert.AreEqual(ObjectMismatchKind.IncompatiblePrefab, mismatches.Single().Kind);
        }

        [Test]
        public void AnEntityThatIsNotAStandaloneEntityIsRefused()
        {
            Connect();

            Assert.Throws<ArgumentException>(() => server.ServerManager.Spawn(new ForeignEntity()));
        }

        [Test]
        public void TheOverriddenPoseIsWhatTheServerSends()
        {
            clientPrefabs.Register(PlayerPrefabId, PlayerFingerprint, () => new StandaloneEntity(PlayerPrefabId, new[] { new CounterBehaviour() }));
            Connect();
            var simulated = new SimulatedEntity(PlayerPrefabId, new Vector3(7, 8, 9));

            server.ServerManager.Spawn(simulated);
            Run(5);

            var onClient = (StandaloneEntity)client.ClientManager.Spawned[simulated.Record.ObjectId];
            Assert.AreEqual(new Vector3(7, 8, 9), onClient.Position);
        }

        [Test]
        public void StateAndRpcsTravelBothWays()
        {
            clientPrefabs.Register(PlayerPrefabId, PlayerFingerprint, () => new StandaloneEntity(PlayerPrefabId, new[] { new CounterBehaviour() }));
            Connect();
            var counter = new CounterBehaviour();
            var player = new StandaloneEntity(PlayerPrefabId, new[] { counter });
            server.ServerManager.Spawn(player);
            Run(5);
            var onClient = (CounterBehaviour)client.ClientManager.Spawned[player.Record.ObjectId].EntityBehaviours[0];

            onClient.Add(3);
            Run(5);
            onClient.Add(4);
            Run(5);

            Assert.AreEqual(7, counter.State.Value);
            Assert.AreEqual(7, onClient.State.Value);
            CollectionAssert.AreEqual(new[] { 3, 7 }, onClient.Echoes);
        }

        [Test]
        public void AGlobalSceneSpawnsTheObjectsOfItsFileOnBothSides()
        {
            files.Add(ArenaSceneId, Door(11, new Vector3(4, 0, 0)), Door(12, new Vector3(-4, 0, 0)));
            Connect();
            var loaded = new List<uint>();
            server.ServerManager.Scenes.OnLoaded += loaded.Add;

            server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
            Run(10);

            CollectionAssert.AreEqual(new[] { ArenaSceneId }, loaded);
            StandaloneEntity[] onServer = server.ServerManager.Spawned.Values.Cast<StandaloneEntity>().OrderBy(entity => entity.SceneObjectId).ToArray();
            CollectionAssert.AreEqual(new ulong[] { 11, 12 }, onServer.Select(entity => entity.SceneObjectId));
            Assert.AreEqual(ArenaSceneId, onServer[0].SceneId);
            foreach (StandaloneEntity door in onServer)
            {
                var onClient = (StandaloneEntity)client.ClientManager.Spawned[door.Record.ObjectId];
                Assert.AreNotSame(door, onClient);
                Assert.AreEqual(door.SceneObjectId, onClient.SceneObjectId);
                Assert.AreEqual(door.Position, onClient.Position);
                Assert.AreEqual(Quaternion.Identity, onClient.Rotation);
                Assert.IsInstanceOf<CounterBehaviour>(onClient.EntityBehaviours.Single());
            }

            Assert.IsEmpty(mismatches);
        }

        [Test]
        public void UnloadingTheSceneDespawnsItsObjectsOnBothSides()
        {
            files.Add(ArenaSceneId, Door(11, Vector3.Zero));
            Connect();
            server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
            Run(10);
            Assert.AreEqual(1, client.ClientManager.Spawned.Count);

            server.ServerManager.Scenes.UnloadGlobal(new[] { ArenaSceneId });
            Run(10);

            Assert.AreEqual(0, server.ServerManager.Spawned.Count);
            Assert.AreEqual(0, client.ClientManager.Spawned.Count);
        }

        [Test]
        public void AFileWrittenForAnotherSceneFileFingerprintFailsTheLoad()
        {
            files.Add(ArenaSceneId, Door(11, Vector3.Zero));
            byte[] bytes = files.Read(ArenaSceneId);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, BinaryPrimitives.ReadUInt64LittleEndian(bytes) ^ 1);
            Connect();
            var failures = new List<Exception>();
            server.ServerManager.Scenes.OnLoadFailed += (sceneId, exception) => failures.Add(exception);

            server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
            Run(5);

            Assert.IsInstanceOf<InvalidDataException>(failures.Single());
            Assert.AreEqual(0, server.ServerManager.Spawned.Count);
            AssertLoggedOnBothSides<InvalidDataException>();
        }

        [Test]
        public void ABehaviourTypeThatIsNotRegisteredFailsTheLoad()
        {
            SceneFileObject door = Door(11, Vector3.Zero);
            door.BehaviourTypes[0] = "Game.Unknown";
            files.Add(ArenaSceneId, door);
            Connect();
            var failures = new List<Exception>();
            server.ServerManager.Scenes.OnLoadFailed += (sceneId, exception) => failures.Add(exception);

            server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
            Run(5);

            Assert.IsInstanceOf<InvalidOperationException>(failures.Single());
            StringAssert.Contains("Game.Unknown", failures[0].Message);
            AssertLoggedOnBothSides<InvalidOperationException>();
        }

        private void AssertLoggedOnBothSides<T>()
            where T : Exception
        {
            Assert.AreEqual(2, logged.Count);
            Assert.IsTrue(logged.All(exception => exception is T));
            logged.Clear();
        }

        private static SceneFileObject Door(ulong sceneObjectId, Vector3 position)
        {
            var door = new SceneFileObject
            {
                SceneObjectId = sceneObjectId,
                Fingerprint = DoorFingerprint,
                Pose = new SceneFilePose
                {
                    PositionX = position.X,
                    PositionY = position.Y,
                    PositionZ = position.Z,
                    RotationW = 1,
                    ScaleX = 1,
                    ScaleY = 1,
                    ScaleZ = 1,
                },
            };
            door.BehaviourTypes.Add(CounterType);
            return door;
        }

        private void Connect()
        {
            var factory = new LoopbackFactory();
            var log = new NetworkLog(logged.Add, message => { });
            server = StandaloneRuntime.Create(registry, new NetworkSettings(), factory, serverPrefabs, behaviours, files, () => now, log);
            client = StandaloneRuntime.Create(registry, new NetworkSettings(), factory, clientPrefabs, behaviours, files, () => now, log);
            client.ClientManager.OnObjectMismatch += mismatches.Add;
            server.ServerManager.StartConnection(7777);
            client.ClientManager.StartConnection("127.0.0.1", 7777);
            Run(10);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        private void Run(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                server.BeginFrame(now, FrameSeconds);
                client.BeginFrame(now, FrameSeconds);
                server.EndFrame();
                client.EndFrame();
            }
        }

        private sealed class Count
        {
            public int Value { get; set; }
        }

        private sealed class CountCodec : IMessageCodec<Count>
        {
            public static readonly CountCodec State = new CountCodec(0x2000_0010);
            public static readonly CountCodec Add = new CountCodec(0x2000_0011);
            public static readonly CountCodec Echo = new CountCodec(0x2000_0012);

            private readonly byte[] buffer = new byte[sizeof(int)];

            private CountCodec(uint messageId)
            {
                MessageId = messageId;
            }

            public static IEnumerable<CountCodec> All => new[] { State, Add, Echo };

            public uint MessageId { get; }

            public static MessageSchema[] Schemas() =>
                All.Select(codec => new MessageSchema(codec.MessageId, codec.MessageId, new ulong[] { codec.MessageId })).ToArray();

            public ReadOnlyMemory<byte> Encode(Count value)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer, value.Value);
                return buffer;
            }

            public void Decode(ReadOnlyMemory<byte> payload, ref Count value)
            {
                value ??= new Count();
                value.Value = BinaryPrimitives.ReadInt32LittleEndian(payload.Span);
            }
        }

        private sealed class CounterBehaviour : EntityBehaviour
        {
            public Count State { get; } = new Count();

            public List<int> Echoes { get; } = new List<int>();

            public void Add(int amount) => SendServerRpc(CountCodec.Add, new Count { Value = amount });

            protected override void OnRegisterState(NetworkState state) => state.Use(CountCodec.State, State);

            protected override void OnRegisterRpcs(NetworkRpcs rpc)
            {
                rpc.OnServer(CountCodec.Add, (peerId, amount) =>
                {
                    State.Value += amount.Value;
                    SendObserversRpc(CountCodec.Echo, new Count { Value = State.Value });
                }, requireOwnership: false);
                rpc.OnClient(CountCodec.Echo, total => Echoes.Add(total.Value));
            }
        }

        private sealed class SimulatedEntity : StandaloneEntity
        {
            private readonly Vector3 simulated;

            public SimulatedEntity(uint prefabId, Vector3 simulated)
                : base(prefabId, new[] { new CounterBehaviour() })
            {
                this.simulated = simulated;
            }

            public override Vector3 Position
            {
                get => simulated;
                set => throw new InvalidOperationException("the simulation owns the position");
            }
        }

        private sealed class ForeignEntity : INetworkEntity
        {
            public uint PrefabId => PlayerPrefabId;

            public ulong SceneObjectId => 0;

            public bool DespawnWithOwner => true;

            public NetworkVisibility Visibility => NetworkVisibility.Rule;

            public IReadOnlyList<EntityBehaviour> EntityBehaviours => Array.Empty<EntityBehaviour>();

            public EntityRecord Record => null;

            public Vector3 ReadWorldPosition() => Vector3.Zero;

            public void ReadRootPose(out Vector3 worldPosition, out Quaternion worldRotation, out Vector3 localScale)
            {
                worldPosition = Vector3.Zero;
                worldRotation = Quaternion.Identity;
                localScale = Vector3.One;
            }

            public void Bind(EntityRecord record)
            {
            }

            public void Unbind(EntityRecord record)
            {
            }
        }

        private sealed class MemorySceneFiles : ISceneFiles
        {
            private readonly Dictionary<uint, byte[]> scenes = new Dictionary<uint, byte[]>();

            public void Add(uint sceneId, params SceneFileObject[] objects)
            {
                var file = new SceneFile { SceneId = sceneId };
                file.Objects.AddRange(objects);
                scenes[sceneId] = SceneFileFormat.Write(TestObjects.Registry(), file);
            }

            public bool Knows(uint sceneId) => scenes.ContainsKey(sceneId);

            public byte[] Read(uint sceneId) => scenes[sceneId];
        }

        private sealed class LoopbackFactory : ITransportFactory
        {
            private readonly LoopbackListener listener = new LoopbackListener(256);

            public int FrameBudget => FomoxaWire.MaxDataFrameSize;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                boundPort = port;
                return listener;
            }

            public ITransportConnector CreateConnector(string address, ushort port) => new Connector(listener);
        }

        private sealed class Connector : ITransportConnector
        {
            private readonly LoopbackListener listener;

            public Connector(LoopbackListener listener)
            {
                this.listener = listener;
            }

            public ConnectStatus Poll(out ITransport transport)
            {
                transport = listener.Connect();
                return ConnectStatus.Connected;
            }

            public void Dispose()
            {
            }
        }
    }
}
