using System;
using System.Collections.Generic;
using System.Numerics;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ClientEntitiesTest
    {
        private const uint PrefabId = 0xA1;
        private const uint Fingerprint = 0xF1;

        [Test]
        public void TheClientCreatesARepresentationThroughItsBackendAndBindsIt()
        {
            var world = new World();
            var served = world.Served();
            world.Server.Spawn(served, 0);
            world.Run(10);

            Assert.AreEqual(1, world.Backend.Created.Count);
            FakeEntity created = world.Backend.Created[0];
            Assert.IsNotNull(created.Record);
            Assert.IsTrue(created.Record.OnClient);
            Assert.IsFalse(created.Record.OnServer);
            Assert.AreEqual(served.Record.ObjectId, created.Record.ObjectId);
            Assert.AreSame(created.Record, world.Client.Spawned[created.Record.ObjectId]);
            CollectionAssert.AreEqual(new[] { "client start client" }, world.ClientCalls);
        }

        [Test]
        public void AServerDespawnStopsAndEndsTheClientRepresentation()
        {
            var world = new World();
            var served = world.Served();
            world.Server.Spawn(served, 0);
            world.Run(10);
            FakeEntity created = world.Backend.Created[0];

            world.Server.Despawn(served);
            world.Run(10);

            Assert.IsNull(created.Record);
            Assert.AreEqual(0, world.Client.Spawned.Count);
            CollectionAssert.AreEqual(new[] { "client start client", "client stop client" }, world.ClientCalls);
            CollectionAssert.AreEqual(new[] { created }, world.Backend.Ended);
        }

        [Test]
        public void AnUnknownPrefabStopsTheClientWithAMismatch()
        {
            var world = new World();
            world.Backend.KnownPrefab = false;
            var mismatches = new List<ObjectMismatchArgs>();
            world.ClientObjects.OnObjectMismatch += mismatches.Add;

            world.Server.Spawn(world.Served(), 0);
            world.Run(10);

            Assert.AreEqual(1, mismatches.Count);
            Assert.AreEqual(ObjectMismatchKind.UnknownPrefab, mismatches[0].Kind);
            Assert.AreEqual(0, world.Backend.Created.Count);
        }

        [Test]
        public void AnOwnerChangeReachesTheClientBehaviours()
        {
            var world = new World();
            var served = world.Served();
            world.Server.Spawn(served, 0);
            world.Run(10);
            FakeEntity created = world.Backend.Created[0];
            ulong localPeerId = world.ClientObjects.LocalPeerId;

            Assert.IsTrue(world.Server.ChangeOwner(served, localPeerId));
            world.Run(10);

            Assert.IsTrue(created.Record.IsOwner);
            Assert.AreEqual(localPeerId, created.Record.OwnerId);
            CollectionAssert.AreEqual(new[] { "client start client", "client owner was 0" }, world.ClientCalls);
        }

        [Test]
        public void ARepresentationDestroyedOnTheClientIsForgottenButTheRowStays()
        {
            var world = new World();
            world.Server.Spawn(world.Served(), 0);
            world.Run(10);
            FakeEntity created = world.Backend.Created[0];
            uint objectId = created.Record.ObjectId;

            Assert.IsTrue(world.Client.ForgetDestroyed(created));

            Assert.IsNull(created.Record);
            Assert.AreEqual(0, world.Client.Spawned.Count);
            Assert.IsTrue(world.ClientObjects.TryGet(objectId, out _));
            Assert.AreEqual(1, world.Warnings.Count);
            CollectionAssert.IsEmpty(world.Backend.Ended);
        }

        private sealed class World
        {
            public readonly List<string> ServerCalls = new List<string>();
            public readonly List<string> ClientCalls = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly FakeClientBackend Backend;
            public readonly ServerSession ServerSession;
            public readonly ServerEntities Server;
            public readonly ClientSession ClientSession;
            public readonly ClientObjects ClientObjects;
            public readonly ClientEntities Client;
            private TimeSpan now;

            public World()
            {
                var log = new NetworkLog(exception => throw exception, Warnings.Add);
                var serverDispatcher = new MessageDispatcher(TestObjects.Schema());
                ServerSession = new ServerSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), serverDispatcher, TestBundles.Protocol(TestObjects.Channels()));
                var serverObjects = new ServerObjects(ServerSession, TestObjects.Protocol(TestObjects.Channels()), Read);
                var clock = new ServerClock(ServerSession, TestObjects.ClockProtocol());
                var inputs = new ServerInputs(ServerSession, serverObjects, TestObjects.InputProtocol(), clock);
                Server = new ServerEntities(this, ServerSession, serverObjects, inputs, serverDispatcher, TestObjects.Channels(), TestObjects.StateProtocol(TestObjects.Channels()), TestObjects.InputProtocol(), new RpcMessageIds(), new FakeServerBackend(), log);
                var listener = new LoopbackListener(8);
                ServerSession.Start(listener);

                Backend = new FakeClientBackend(ClientCalls);
                var clientDispatcher = new MessageDispatcher(TestObjects.Schema());
                ClientSession = new ClientSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), clientDispatcher, TestBundles.Protocol(TestObjects.Channels()));
                Client = new ClientEntities(this, ClientSession, clientDispatcher, TestObjects.Channels(), TestObjects.StateProtocol(TestObjects.Channels()), new RpcMessageIds(), null, Backend, log);
                ClientObjects = new ClientObjects(ClientSession, TestObjects.Protocol(TestObjects.Channels()), Client);
                Client.Attach(ClientObjects);
                ClientSession.Start(listener.Connect(), now);
                Run(20);
            }

            public FakeEntity Served() => new FakeEntity(PrefabId, new RecordingBehaviour("server", ServerCalls));

            public void Run(int steps)
            {
                for (int step = 0; step < steps; step++)
                {
                    now += TimeSpan.FromMilliseconds(16);
                    ServerSession.Tick(now);
                    ClientSession.Tick(now);
                    ServerSession.Flush();
                    ClientSession.Flush();
                }
            }

            private SpawnData Read(uint objectId)
            {
                EntityRecord record = Server.Spawning;
                if (record == null)
                {
                    Server.TryGet(objectId, out record);
                }

                var states = new List<ReadOnlyMemory<byte>>();
                foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
                {
                    states.Add(behaviour.StateSlot?.Sent ?? ReadOnlyMemory<byte>.Empty);
                }

                return new SpawnData(record.Fingerprint, Vector3.Zero, Quaternion.Identity, Vector3.One, states);
            }
        }

        private sealed class FakeServerBackend : IServerEntityBackend
        {
            public string NameOf(INetworkEntity entity) => "served";

            public void ValidateSpawn(INetworkEntity entity)
            {
            }

            public uint FingerprintOf(INetworkEntity entity, bool isSceneObject) => Fingerprint;

            public uint SceneIdOf(INetworkEntity entity) => 0;

            public void PrepareSpawn(INetworkEntity entity)
            {
            }

            public void Activate(INetworkEntity entity)
            {
            }

            public void End(INetworkEntity entity, bool isSceneObject)
            {
            }
        }

        private sealed class FakeClientBackend : IClientEntityBackend
        {
            public readonly List<FakeEntity> Created = new List<FakeEntity>();
            public readonly List<FakeEntity> Ended = new List<FakeEntity>();
            private readonly List<string> calls;

            public FakeClientBackend(List<string> calls)
            {
                this.calls = calls;
            }

            public bool KnownPrefab { get; set; } = true;

            public SpawnResult CheckPrefab(in SpawnedObject spawned)
            {
                if (!KnownPrefab)
                {
                    return SpawnResult.UnknownPrefab;
                }

                return spawned.PrefabFingerprint == Fingerprint ? SpawnResult.Spawned : SpawnResult.IncompatiblePrefab;
            }

            public INetworkEntity Create(in SpawnedObject spawned)
            {
                var entity = new FakeEntity(spawned.PrefabId, new RecordingBehaviour("client", calls));
                Created.Add(entity);
                return entity;
            }

            public SpawnResult PlaceSceneObject(in SpawnedObject spawned, out INetworkEntity entity)
            {
                entity = null;
                return SpawnResult.UnknownSceneObject;
            }

            public void PrepareReceive(INetworkEntity entity)
            {
            }

            public void End(INetworkEntity entity) => Ended.Add((FakeEntity)entity);

            public void HideOnHost(INetworkEntity entity)
            {
            }

            public void ShowOnHost(INetworkEntity entity)
            {
            }
        }

        private sealed class FakeEntity : INetworkEntity
        {
            private readonly EntityBehaviour[] behaviours;

            public FakeEntity(uint prefabId, params EntityBehaviour[] behaviours)
            {
                PrefabId = prefabId;
                this.behaviours = behaviours;
                for (int index = 0; index < behaviours.Length; index++)
                {
                    ((RecordingBehaviour)behaviours[index]).Attach(index);
                }
            }

            public uint PrefabId { get; }

            public ulong SceneObjectId => 0;

            public bool DespawnWithOwner => true;

            public NetworkVisibility Visibility => NetworkVisibility.Rule;

            public IReadOnlyList<EntityBehaviour> EntityBehaviours => behaviours;

            public EntityRecord Record { get; private set; }

            public void Bind(EntityRecord record) => Record = record;

            public void Unbind(EntityRecord record)
            {
                if (Record == record)
                {
                    Record = null;
                }
            }
        }

        private sealed class RecordingBehaviour : EntityBehaviour
        {
            private readonly string name;
            private readonly List<string> calls;

            public RecordingBehaviour(string name, List<string> calls)
            {
                this.name = name;
                this.calls = calls;
            }

            public void Attach(int index) => Attach(null, (byte)index);

            public override void OnStartClient() => calls.Add($"{name} start client");

            public override void OnStopClient() => calls.Add($"{name} stop client");

            public override void OnOwnerChangedClient(ulong previousOwnerId) => calls.Add($"{name} owner was {previousOwnerId}");
        }
    }
}
