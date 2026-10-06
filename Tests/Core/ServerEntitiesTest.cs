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
    public sealed class ServerEntitiesTest
    {
        private const uint PrefabId = 0xA1;
        private const uint Fingerprint = 0xF1;

        [Test]
        public void SpawnBindsTheRecordAndStartsTheBehaviours()
        {
            var host = new Host();
            var entity = new FakeEntity(PrefabId, host.Behaviour("a"), host.Behaviour("b"));

            host.Entities.Spawn(entity, 0);

            Assert.IsNotNull(entity.Record);
            Assert.IsTrue(entity.Record.OnServer);
            Assert.AreNotEqual(0u, entity.Record.ObjectId);
            Assert.AreSame(entity, entity.Record.Representation);
            Assert.AreSame(entity.Record, host.Entities.Spawned[entity.Record.ObjectId]);
            Assert.IsTrue(host.Objects.TryGet(entity.Record.ObjectId, out ObjectRow row));
            Assert.AreEqual(PrefabId, row.PrefabId);
            CollectionAssert.AreEqual(new[] { "a start server", "b start server" }, host.Calls);
            CollectionAssert.AreEqual(new[] { "prepare" }, host.Backend.Calls);
        }

        [Test]
        public void TheBackendFingerprintGoesIntoTheRecord()
        {
            var host = new Host();
            var entity = new FakeEntity(PrefabId);

            host.Entities.Spawn(entity, 0);

            Assert.AreEqual(Fingerprint, entity.Record.Fingerprint);
        }

        [Test]
        public void SpawnBeforeTheServerStartsThrows()
        {
            var host = new Host(start: false);

            var error = Assert.Throws<InvalidOperationException>(() => host.Entities.Spawn(new FakeEntity(PrefabId), 0));

            Assert.AreEqual("the server is not started", error.Message);
        }

        [Test]
        public void SpawningTheSameRepresentationTwiceThrows()
        {
            var host = new Host();
            var entity = new FakeEntity(PrefabId);
            host.Entities.Spawn(entity, 0);

            var error = Assert.Throws<ArgumentException>(() => host.Entities.Spawn(entity, 0));

            StringAssert.StartsWith("fake 0xA1 is already spawned", error.Message);
        }

        [Test]
        public void DespawnStopsUnbindsAndEndsTheRepresentation()
        {
            var host = new Host();
            var entity = new FakeEntity(PrefabId, host.Behaviour("a"));
            host.Entities.Spawn(entity, 0);
            uint objectId = entity.Record.ObjectId;

            Assert.IsTrue(host.Entities.Despawn(entity));

            Assert.IsNull(entity.Record);
            Assert.IsFalse(host.Entities.Spawned.ContainsKey(objectId));
            Assert.IsFalse(host.Objects.TryGet(objectId, out _));
            CollectionAssert.AreEqual(new[] { "a start server", "a stop server" }, host.Calls);
            CollectionAssert.AreEqual(new[] { "prepare", "end prefab" }, host.Backend.Calls);
            Assert.IsFalse(host.Entities.Despawn(entity));
        }

        [Test]
        public void ADestroyedRepresentationIsDespawnedWithoutEndingIt()
        {
            var host = new Host();
            var entity = new FakeEntity(PrefabId, host.Behaviour("a"));
            host.Entities.Spawn(entity, 0);

            host.Entities.DespawnDestroyed(entity);

            Assert.IsNull(entity.Record);
            Assert.AreEqual(0, host.Entities.Spawned.Count);
            CollectionAssert.AreEqual(new[] { "a start server", "a stop server" }, host.Calls);
            CollectionAssert.AreEqual(new[] { "prepare" }, host.Backend.Calls);
        }

        [Test]
        public void StoppingTheServerDespawnsInReverseSpawnOrder()
        {
            var host = new Host();
            host.Entities.Spawn(new FakeEntity(PrefabId, host.Behaviour("first")), 0);
            host.Entities.Spawn(new FakeEntity(PrefabId, host.Behaviour("second")), 0);
            host.Calls.Clear();

            host.Session.Stop();

            CollectionAssert.AreEqual(new[] { "second stop server", "first stop server" }, host.Calls);
            Assert.AreEqual(0, host.Entities.Spawned.Count);
        }

        [Test]
        public void ASceneObjectIsActivatedOnSpawnAndDeactivatedOnDespawn()
        {
            var host = new Host();
            var sceneObject = new FakeEntity(0, sceneObjectId: 0xABCD_0001_0000_0007);
            host.Entities.SpawnSceneObjects(new[] { sceneObject });

            Assert.IsNotNull(sceneObject.Record);
            Assert.IsTrue(host.Entities.Despawn(sceneObject));
            CollectionAssert.AreEqual(new[] { "prepare", "activate", "end scene object" }, host.Backend.Calls);
        }

        private sealed class Host
        {
            public readonly List<string> Calls = new List<string>();
            public readonly FakeBackend Backend = new FakeBackend();
            public readonly ServerSession Session;
            public readonly ServerObjects Objects;
            public readonly ServerEntities Entities;

            public Host(bool start = true)
            {
                var dispatcher = new MessageDispatcher(TestObjects.Schema());
                Session = new ServerSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), dispatcher, TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ServerObjects(Session, TestObjects.Protocol(TestObjects.Channels()), Read);
                var clock = new ServerClock(Session, TestObjects.ClockProtocol());
                var inputs = new ServerInputs(Session, Objects, TestObjects.InputProtocol(), clock);
                Entities = new ServerEntities(
                    this,
                    Session,
                    Objects,
                    inputs,
                    dispatcher,
                    TestObjects.Channels(),
                    TestObjects.StateProtocol(TestObjects.Channels()),
                    TestObjects.InputProtocol(),
                    new RpcMessageIds(),
                    Backend,
                    new NetworkLog(exception => throw exception, message => { }));
                if (start)
                {
                    Session.Start(new LoopbackListener(8));
                }
            }

            public EntityBehaviour Behaviour(string name) => new RecordingBehaviour(name, Calls);

            private SpawnData Read(uint objectId) => new SpawnData(Fingerprint, Vector3.Zero, Quaternion.Identity, Vector3.One);
        }

        private sealed class FakeBackend : IServerEntityBackend
        {
            public readonly List<string> Calls = new List<string>();

            public string NameOf(INetworkEntity entity) => $"fake 0x{entity.PrefabId:X2}";

            public void ValidateSpawn(INetworkEntity entity)
            {
            }

            public uint FingerprintOf(INetworkEntity entity, bool isSceneObject) => Fingerprint;

            public uint SceneIdOf(INetworkEntity entity) => 0;

            public void PrepareSpawn(INetworkEntity entity) => Calls.Add("prepare");

            public void Activate(INetworkEntity entity) => Calls.Add("activate");

            public void End(INetworkEntity entity, bool isSceneObject) => Calls.Add(isSceneObject ? "end scene object" : "end prefab");
        }

        private sealed class FakeEntity : INetworkEntity
        {
            private readonly EntityBehaviour[] behaviours;

            public FakeEntity(uint prefabId, params EntityBehaviour[] behaviours)
                : this(prefabId, 0, behaviours)
            {
            }

            public FakeEntity(uint prefabId, ulong sceneObjectId, params EntityBehaviour[] behaviours)
            {
                PrefabId = prefabId;
                SceneObjectId = sceneObjectId;
                this.behaviours = behaviours;
                for (int index = 0; index < behaviours.Length; index++)
                {
                    ((RecordingBehaviour)behaviours[index]).Attach(index);
                }
            }

            public uint PrefabId { get; }

            public ulong SceneObjectId { get; }

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

            public override void OnStartServer() => calls.Add($"{name} start server");

            public override void OnStopServer() => calls.Add($"{name} stop server");
        }
    }
}
