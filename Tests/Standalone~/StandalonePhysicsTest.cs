using System;
using System.Collections.Generic;
using System.Numerics;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Standalone.Tests
{
    public sealed class StandalonePhysicsTest
    {
        private const uint BallPrefabId = 0x0000_0B01;
        private const uint BallFingerprint = 0x0000_0BA1;
        private const uint ArenaSceneId = 0x00A1_E7A0;
        private const double FrameSeconds = 1.0 / 30;

        private FomoxaRegistry registry;
        private SceneFiles files;
        private TimeSpan now;
        private NetworkRuntime server;
        private NetworkRuntime client;
        private StandalonePrefabs prefabs;
        private FakePhysicsScenes serverPhysics;
        private FakePhysicsScenes clientPhysics;
        private List<Exception> logged;

        [SetUp]
        public void SetUp()
        {
            registry = TestObjects.Registry();
            files = new SceneFiles();
            prefabs = new StandalonePrefabs();
            prefabs.Register(BallPrefabId, BallFingerprint, () => new BallEntity());
            serverPhysics = new FakePhysicsScenes();
            clientPhysics = new FakePhysicsScenes();
            logged = new List<Exception>();
        }

        [TearDown]
        public void TearDown()
        {
            Assert.IsEmpty(logged);
        }

        [Test]
        public void ASceneWithGeometryLoadsIntoThePhysicsOfBothSidesAndItsObjectsGetTheirBodies()
        {
            var file = new SceneFile { SceneId = ArenaSceneId };
            file.Colliders.Add(SceneFileGeometry.ToFile(new ColliderDesc(BodyShape.Box(Vector3.One), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false)));
            file.Objects.Add(Crate(21, new Vector2(4f, 0f), 0.5f));
            files.Add(file);
            Connect();

            server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
            Run(10);

            Assert.AreEqual(1, serverPhysics.Loaded.Count);
            Assert.AreEqual(1, serverPhysics.Loaded[0].Colliders.Count);
            Assert.AreEqual(1, clientPhysics.Loaded.Count);
            var onServer = (StandaloneEntity)server.ServerManager.Spawned[FindObjectId(server, 21)];
            var onClient = (StandaloneEntity)client.ClientManager.Spawned[FindObjectId(server, 21)];
            Assert.IsTrue(onServer.Body2D.IsValid);
            Assert.IsTrue(onClient.Body2D.IsValid);
            Assert.AreEqual(0.5f, serverPhysics.Created2D[0].Rotation);
            Assert.AreEqual(new Vector3(4f, 0f, 0f), onServer.Position);
            CollectionAssert.Contains(serverPhysics.Calls, $"body2D 0x{ArenaSceneId:X8}");
        }

        [Test]
        public void ThePoseOfAnEntityWithABodyIsThePoseOfTheBody()
        {
            Connect();
            var ball = new BallEntity { Position = new Vector3(1f, 2f, 3f) };

            server.ServerManager.Spawn(ball);
            Run(2);
            PhysicsBody body = ball.Body;
            body.Velocity = new Vector3(3f, 0f, 0f);
            Run(10);

            Assert.IsTrue(ball.Body.IsValid);
            Assert.Greater(ball.Position.X, 1f);
            Assert.AreEqual(ball.Body.Position, ball.Position);
            ball.Position = new Vector3(-5f, 0f, 0f);
            Assert.AreEqual(new Vector3(-5f, 0f, 0f), ball.Body.Position);
            Assert.IsTrue(((StandaloneEntity)client.ClientManager.Spawned[ball.Record.ObjectId]).Body.IsValid);
        }

        [Test]
        public void ADespawnedEntityLosesItsBodiesAndKeepsItsLastPose()
        {
            Connect();
            var ball = new BallEntity();
            server.ServerManager.Spawn(ball);
            Run(2);
            PhysicsBody body = ball.Body;
            body.Position = new Vector3(6f, 0f, 0f);

            server.ServerManager.Despawn(ball);
            Run(2);

            Assert.IsFalse(ball.Body.IsValid);
            Assert.AreEqual(new Vector3(6f, 0f, 0f), ball.Position);
            Assert.AreEqual(0, serverPhysics.World.Count);
            Assert.AreEqual(0, clientPhysics.World.Count);
        }

        [Test]
        public void UnloadingASceneUnloadsItsGeometryAndTheBodiesOfItsObjects()
        {
            var file = new SceneFile { SceneId = ArenaSceneId };
            file.Objects.Add(Crate(21, Vector2.Zero, 0f));
            files.Add(file);
            Connect();
            server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
            Run(10);

            server.ServerManager.Scenes.UnloadGlobal(new[] { ArenaSceneId });
            Run(10);

            CollectionAssert.Contains(serverPhysics.Calls, $"unload 0x{ArenaSceneId:X8}");
            CollectionAssert.Contains(clientPhysics.Calls, $"unload 0x{ArenaSceneId:X8}");
            Assert.AreEqual(0, serverPhysics.World2D.Count);
            Assert.AreEqual(0, clientPhysics.World2D.Count);
        }

        [Test]
        public void ThePredictionBackendAsksThePhysicsForWorldsHistoriesAndProxies()
        {
            var physics = new FakePhysicsScenes();
            var prediction = new StandalonePredictionBackend(physics);
            var ball = new BallEntity();
            ball.AttachBodies(physics);
            var worlds = new List<IPhysicsSimulation>();

            prediction.WorldsOf(ball, worlds);
            prediction.PlaceProxy(ball);
            prediction.EndProxy(ball);

            CollectionAssert.AreEqual(new IPhysicsSimulation[] { physics.World }, worlds);
            Assert.AreSame(prediction.HistoryOf(physics.World, 4), physics.HistoryOf(physics.World, 4));
            CollectionAssert.IsSubsetOf(new[] { "proxy", "end proxy" }, physics.Calls);
            Assert.Throws<NotSupportedException>(() => new StandalonePredictionBackend(null).HistoryOf(physics.World, 4));
        }

        [Test]
        public void PhysicsOfAnotherBackendIsRefused()
        {
            var log = new NetworkLog(logged.Add, message => { });

            Assert.Throws<ArgumentException>(() => StandaloneRuntime.Create(registry, new NetworkSettings(), new LoopbackFactory(), prefabs, new StandaloneBehaviours(), files, () => now, log, serverPhysics));
        }

        private static SceneFileObject Crate(ulong sceneObjectId, Vector2 position, float angle)
        {
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
            return new SceneFileObject
            {
                SceneObjectId = sceneObjectId,
                Fingerprint = 0xC0,
                Pose = new SceneFilePose { PositionX = position.X, PositionY = position.Y, RotationX = rotation.X, RotationY = rotation.Y, RotationZ = rotation.Z, RotationW = rotation.W, ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f },
                Body2D = SceneFileGeometry.ToFile(new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Circle(0.5f), position, angle, 1f)),
            };
        }

        private static uint FindObjectId(NetworkRuntime runtime, ulong sceneObjectId)
        {
            foreach (KeyValuePair<uint, INetworkEntity> entry in runtime.ServerManager.Spawned)
            {
                if (entry.Value.SceneObjectId == sceneObjectId)
                {
                    return entry.Key;
                }
            }

            Assert.Fail($"scene object {sceneObjectId} is not spawned");
            return 0;
        }

        private void Connect()
        {
            var factory = new LoopbackFactory();
            var log = new NetworkLog(logged.Add, message => { });
            var settings = new NetworkSettings { PhysicsBackend = PhysicsBackend.Rapier };
            server = StandaloneRuntime.Create(registry, settings, factory, prefabs, new StandaloneBehaviours(), files, () => now, log, serverPhysics);
            client = StandaloneRuntime.Create(registry, settings, factory, prefabs, new StandaloneBehaviours(), files, () => now, log, clientPhysics);
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

        private sealed class BallEntity : StandaloneEntity
        {
            public BallEntity()
                : base(BallPrefabId, Array.Empty<EntityBehaviour>())
            {
            }

            public override bool TryGetBody(out BodyDesc body)
            {
                body = new BodyDesc(BodyKind.Dynamic, BodyShape.Sphere(0.5f), Vector3.Zero, Quaternion.Identity, 1f);
                return true;
            }
        }

        private sealed class FakePhysicsScenes : IPhysicsScenes
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<SceneFile> Loaded = new List<SceneFile>();
            public readonly List<BodyDesc2D> Created2D = new List<BodyDesc2D>();
            public readonly FakeWorld World = new FakeWorld();
            public readonly FakeWorld2D World2D = new FakeWorld2D();
            private readonly Dictionary<INetworkEntity, List<BodyHandle>> bodies = new Dictionary<INetworkEntity, List<BodyHandle>>();
            private readonly Dictionary<INetworkEntity, List<BodyHandle>> bodies2D = new Dictionary<INetworkEntity, List<BodyHandle>>();
            private readonly Dictionary<IPhysicsSimulation, PhysicsHistory> histories = new Dictionary<IPhysicsSimulation, PhysicsHistory>();

            public PhysicsBackend Backend => PhysicsBackend.Rapier;

            public void LoadScene(SceneFile file)
            {
                Loaded.Add(file);
                Calls.Add($"load 0x{file.SceneId:X8}");
            }

            public void UnloadScene(uint sceneId) => Calls.Add($"unload 0x{sceneId:X8}");

            public PhysicsBody AddBody(INetworkEntity entity, uint sceneId, in BodyDesc body)
            {
                Calls.Add($"body 0x{sceneId:X8}");
                BodyHandle handle = World.CreateBody(body);
                Track(bodies, entity, handle);
                return new PhysicsBody(World, handle);
            }

            public PhysicsBody2D AddBody2D(INetworkEntity entity, uint sceneId, in BodyDesc2D body)
            {
                Calls.Add($"body2D 0x{sceneId:X8}");
                Created2D.Add(body);
                BodyHandle handle = World2D.CreateBody(body);
                Track(bodies2D, entity, handle);
                return new PhysicsBody2D(World2D, handle);
            }

            public void RemoveBodies(INetworkEntity entity)
            {
                Calls.Add("remove");
                if (bodies.Remove(entity, out List<BodyHandle> handles))
                {
                    handles.ForEach(handle => World.RemoveBody(handle));
                }

                if (bodies2D.Remove(entity, out List<BodyHandle> handles2D))
                {
                    handles2D.ForEach(handle => World2D.RemoveBody(handle));
                }
            }

            public void WorldsOf(INetworkEntity entity, List<IPhysicsSimulation> worlds)
            {
                if (bodies.ContainsKey(entity))
                {
                    worlds.Add(World);
                }

                if (bodies2D.ContainsKey(entity))
                {
                    worlds.Add(World2D);
                }
            }

            public PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity)
            {
                if (!histories.TryGetValue(world, out PhysicsHistory history))
                {
                    history = new PhysicsHistory(world, capacity);
                    histories.Add(world, history);
                }

                return history;
            }

            public void PlaceProxy(INetworkEntity entity) => Calls.Add("proxy");

            public void EndProxy(INetworkEntity entity) => Calls.Add("end proxy");

            public void WorldsToStep(List<IPhysicsSimulation> worlds)
            {
                worlds.Add(World);
                worlds.Add(World2D);
            }

            public IContactTracker TrackerOf(IPhysicsSimulation world) => null;

            private static void Track(Dictionary<INetworkEntity, List<BodyHandle>> owned, INetworkEntity entity, BodyHandle handle)
            {
                if (!owned.TryGetValue(entity, out List<BodyHandle> handles))
                {
                    handles = new List<BodyHandle>();
                    owned.Add(entity, handles);
                }

                handles.Add(handle);
            }
        }

        private sealed class FakeWorld : IPhysicsWorld
        {
            private readonly Dictionary<int, BodyState> states = new Dictionary<int, BodyState>();
            private readonly Dictionary<int, BodyDesc> descs = new Dictionary<int, BodyDesc>();
            private int next = 1;

            public int Count => states.Count;

            public PhysicsBackend Backend => PhysicsBackend.Rapier;

            public void Step(float seconds)
            {
                foreach (int handle in new List<int>(states.Keys))
                {
                    if (descs[handle].Kind == BodyKind.Dynamic)
                    {
                        BodyState state = states[handle];
                        state.Position += state.Velocity * seconds;
                        states[handle] = state;
                    }
                }
            }

            public PhysicsSnapshot CreateSnapshot() => new Snapshot();

            public void Save(PhysicsSnapshot into)
            {
                ((Snapshot)into).States = new Dictionary<int, BodyState>(states);
            }

            public void Load(PhysicsSnapshot from)
            {
                foreach (KeyValuePair<int, BodyState> entry in ((Snapshot)from).States)
                {
                    if (states.ContainsKey(entry.Key))
                    {
                        states[entry.Key] = entry.Value;
                    }
                }
            }

            public BodyHandle CreateBody(in BodyDesc desc)
            {
                int handle = next++;
                descs.Add(handle, desc);
                states.Add(handle, new BodyState { Position = desc.Position, Rotation = desc.Rotation });
                return new BodyHandle(handle);
            }

            public bool RemoveBody(BodyHandle body) => states.Remove(body.Value) & descs.Remove(body.Value);

            public bool Contains(BodyHandle body) => states.ContainsKey(body.Value);

            public BodyState GetBody(BodyHandle body) => states[body.Value];

            public BodyKind GetKind(BodyHandle body) => descs[body.Value].Kind;

            public float GetMass(BodyHandle body) => descs[body.Value].Mass;

            public void SetBody(BodyHandle body, in BodyState state) => states[body.Value] = state;

            public void SetRewindable(BodyHandle body, bool rewindable)
            {
            }

            public void AddForce(BodyHandle body, Vector3 force)
            {
            }

            public void AddImpulse(BodyHandle body, Vector3 impulse)
            {
                BodyState state = states[body.Value];
                state.Velocity += impulse / descs[body.Value].Mass;
                states[body.Value] = state;
            }

            public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit)
            {
                hit = default;
                return false;
            }

            public int Overlap(Vector3 center, float radius, BodyHandle[] results) => 0;

            private sealed class Snapshot : PhysicsSnapshot
            {
                public Dictionary<int, BodyState> States = new Dictionary<int, BodyState>();
            }
        }

        private sealed class FakeWorld2D : IPhysicsWorld2D
        {
            private readonly Dictionary<int, BodyState2D> states = new Dictionary<int, BodyState2D>();
            private readonly Dictionary<int, BodyDesc2D> descs = new Dictionary<int, BodyDesc2D>();
            private int next = 1;

            public int Count => states.Count;

            public PhysicsBackend Backend => PhysicsBackend.Rapier;

            public void Step(float seconds)
            {
                foreach (int handle in new List<int>(states.Keys))
                {
                    if (descs[handle].Kind == BodyKind.Dynamic)
                    {
                        BodyState2D state = states[handle];
                        state.Position += state.Velocity * seconds;
                        states[handle] = state;
                    }
                }
            }

            public PhysicsSnapshot CreateSnapshot() => new Snapshot();

            public void Save(PhysicsSnapshot into)
            {
                ((Snapshot)into).States = new Dictionary<int, BodyState2D>(states);
            }

            public void Load(PhysicsSnapshot from)
            {
                foreach (KeyValuePair<int, BodyState2D> entry in ((Snapshot)from).States)
                {
                    if (states.ContainsKey(entry.Key))
                    {
                        states[entry.Key] = entry.Value;
                    }
                }
            }

            public BodyHandle CreateBody(in BodyDesc2D desc)
            {
                int handle = next++;
                descs.Add(handle, desc);
                states.Add(handle, new BodyState2D { Position = desc.Position, Rotation = desc.Rotation });
                return new BodyHandle(handle);
            }

            public bool RemoveBody(BodyHandle body) => states.Remove(body.Value) & descs.Remove(body.Value);

            public bool Contains(BodyHandle body) => states.ContainsKey(body.Value);

            public BodyState2D GetBody(BodyHandle body) => states[body.Value];

            public BodyKind GetKind(BodyHandle body) => descs[body.Value].Kind;

            public float GetMass(BodyHandle body) => descs[body.Value].Mass;

            public void SetBody(BodyHandle body, in BodyState2D state) => states[body.Value] = state;

            public void SetRewindable(BodyHandle body, bool rewindable)
            {
            }

            public void AddForce(BodyHandle body, Vector2 force)
            {
            }

            public void AddImpulse(BodyHandle body, Vector2 impulse)
            {
                BodyState2D state = states[body.Value];
                state.Velocity += impulse / descs[body.Value].Mass;
                states[body.Value] = state;
            }

            public bool Raycast(Vector2 origin, Vector2 direction, float maxDistance, out RayHit2D hit)
            {
                hit = default;
                return false;
            }

            public int Overlap(Vector2 center, float radius, BodyHandle[] results) => 0;

            private sealed class Snapshot : PhysicsSnapshot
            {
                public Dictionary<int, BodyState2D> States = new Dictionary<int, BodyState2D>();
            }
        }

        private sealed class SceneFiles : ISceneFiles
        {
            private readonly Dictionary<uint, byte[]> scenes = new Dictionary<uint, byte[]>();

            public void Add(SceneFile file) => scenes[file.SceneId] = SceneFileFormat.Write(TestObjects.Registry(), file);

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
