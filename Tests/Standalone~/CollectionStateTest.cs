using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Standalone.Tests
{
    public sealed class CollectionStateTest
    {
        private const uint PrefabId = 0x0000_0601;
        private const uint Fingerprint = 0x0000_F1C0;
        private const double FrameSeconds = 1.0 / 30;

        private FomoxaRegistry registry;
        private StandaloneBackendTest.LoopbackFactory factory;
        private TimeSpan now;
        private NetworkRuntime server;
        private List<NetworkRuntime> clients;
        private StandalonePrefabs serverPrefabs;
        private StandalonePrefabs clientPrefabs;
        private List<Inventory> created;
        private List<ObjectMismatchArgs> mismatches;
        private List<Exception> logged;

        [SetUp]
        public void SetUp()
        {
            registry = TestObjects.Registry(new MessageSchema(LevelCodec.Instance.MessageId, LevelCodec.Instance.MessageId, new ulong[] { LevelCodec.Instance.MessageId }));
            registry.Channels.Set(LevelCodec.Instance.MessageId, Channel.ReliableOrdered);
            factory = new StandaloneBackendTest.LoopbackFactory();
            serverPrefabs = new StandalonePrefabs();
            clientPrefabs = new StandalonePrefabs();
            clients = new List<NetworkRuntime>();
            created = new List<Inventory>();
            mismatches = new List<ObjectMismatchArgs>();
            logged = new List<Exception>();
            foreach (uint prefabId in new[] { PrefabId, PrefabId + 2, PrefabId + 3 })
            {
                uint registered = prefabId;
                serverPrefabs.Register(registered, Fingerprint, () => new StandaloneEntity(registered, new EntityBehaviour[] { new Inventory() }));
            }

            clientPrefabs.Register(PrefabId, Fingerprint, () =>
            {
                var inventory = new Inventory();
                created.Add(inventory);
                return new StandaloneEntity(PrefabId, new EntityBehaviour[] { inventory });
            });
        }

        [TearDown]
        public void TearDown()
        {
            Assert.IsEmpty(logged);
        }

        [Test]
        public void ContentFilledBeforeSpawnArrivesWithTheSpawnWithoutCallbacks()
        {
            Connect();
            var inventory = new Inventory();
            inventory.Items.Add(Item(1, 10));
            inventory.Scores["a"] = 1;
            inventory.Tags.Add(7);
            inventory.Slots[3] = Item(9, 90);

            Spawn(inventory);
            Run(5);

            Assert.AreEqual(inventory.Describe(), created.Single().StartedWith);
            CollectionAssert.IsEmpty(created.Single().Changes);
        }

        [Test]
        public void EachOperationReachesTheClientAfterItIsAppliedInDeclaredIndexOrder()
        {
            Connect();
            var inventory = new Inventory();
            Spawn(inventory);
            Run(5);
            var third = Item(3, 30);

            inventory.Tags.Add(7);
            Assert.IsFalse(inventory.Tags.Add(7));
            inventory.Tags.Add(8);
            inventory.Tags.Remove(7);
            inventory.Items.Add(Item(1, 10));
            inventory.Items.Add(Item(2, 20));
            inventory.Items.Insert(0, third);
            inventory.Items[1] = Item(4, 40);
            inventory.Items.RemoveAt(2);
            Assert.IsTrue(inventory.Items.Remove(third));
            Assert.IsFalse(inventory.Items.Remove(third));
            inventory.Items.Insert(1, Item(5, 50));
            inventory.Scores.Add("a", 1);
            inventory.Scores["a"] = 2;
            inventory.Scores["b"] = 3;
            Assert.IsTrue(inventory.Scores.Remove("a"));
            Assert.IsFalse(inventory.Scores.Remove("a"));
            Run(5);

            Inventory onClient = created.Single();
            Assert.AreEqual(inventory.Describe(), onClient.Describe());
            CollectionAssert.AreEqual(new[]
            {
                "scores Add a 0 1",
                "scores Set a 1 2",
                "scores Add b 0 3",
                "scores Remove a 2 0",
                "items Add 0 - 1:10",
                "items Add 1 - 2:20",
                "items Insert 0 - 3:30",
                "items Set 1 1:10 4:40",
                "items RemoveAt 2 2:20 -",
                "items RemoveAt 0 3:30 -",
                "items Insert 1 - 5:50",
                "tags Add 0 7",
                "tags Add 0 8",
                "tags Remove 7 0",
            }, onClient.Changes);
        }

        [Test]
        public void DirtyAndModifySendTheElementAgainAsASet()
        {
            Connect();
            var inventory = new Inventory();
            inventory.Items.Add(Item(1, 10));
            inventory.Slots[3] = Item(9, 90);
            Spawn(inventory);
            Run(5);
            Inventory onClient = created.Single();

            inventory.Items[0].OwnerId = 11;
            inventory.Items.Dirty(0);
            inventory.Items.Modify(0, item => item.OwnerId = 12);
            inventory.Items.Modify(0, 13UL, (item, value) => item.OwnerId = value);
            inventory.Slots.Modify(3, 91UL, (item, value) => item.OwnerId = value);
            inventory.Slots[3].OwnerId = 92;
            inventory.Slots.Dirty(3);
            Run(5);

            Assert.AreEqual(inventory.Describe(), onClient.Describe());
            CollectionAssert.AreEqual(new[]
            {
                "items Set 0 1:10 1:11",
                "items Set 0 1:11 1:12",
                "items Set 0 1:12 1:13",
                "slots Set 3 9:90 9:91",
                "slots Set 3 9:91 9:92",
            }, onClient.Changes);
        }

        [Test]
        public void AModifyThatThrowsStillSendsTheElement()
        {
            Connect();
            var inventory = new Inventory();
            inventory.Items.Add(Item(1, 10));
            Spawn(inventory);
            Run(5);

            Assert.Throws<InvalidOperationException>(() => inventory.Items.Modify(0, item =>
            {
                item.OwnerId = 14;
                throw new InvalidOperationException("partial");
            }));
            Run(5);

            Assert.AreEqual("1:14", Text(created.Single().Items[0]));
        }

        [Test]
        public void AFieldChangedWithoutMarkingIsNotSent()
        {
            Connect();
            var inventory = new Inventory();
            inventory.Items.Add(Item(1, 10));
            Spawn(inventory);
            Run(5);

            inventory.Items[0].OwnerId = 15;
            Run(5);

            Assert.AreEqual("1:10", Text(created.Single().Items[0]));
            CollectionAssert.IsEmpty(created.Single().Changes);
        }

        [Test]
        public void AClearDropsTheOperationsWaitingBeforeIt()
        {
            Connect();
            var inventory = new Inventory();
            Spawn(inventory);
            Run(5);

            inventory.Items.Add(Item(1, 10));
            inventory.Items.Add(Item(2, 20));
            inventory.Items.Clear();
            inventory.Items.Add(Item(3, 30));
            Run(5);

            CollectionAssert.AreEqual(new[] { "items Clear -1 - -", "items Add 0 - 3:30" }, created.Single().Changes);
            Assert.AreEqual(inventory.Describe(), created.Single().Describe());
        }

        [Test]
        public void APeerThatEntersBetweenTwoSendsTakesTheWaitingOperationsFromTheSpawnOnly()
        {
            var rule = new PeerRule();
            Connect(2);
            server.ServerManager.ObserverRule = rule;
            rule.Allowed.Add(PeerIdOf(clients[0]));
            var inventory = new Inventory();
            Spawn(inventory);
            Run(5);

            inventory.Items.Add(Item(1, 10));
            rule.Allowed.Add(PeerIdOf(clients[1]));
            server.ServerManager.RebuildObservers();
            inventory.Items.Add(Item(2, 20));
            Run(5);

            Assert.AreEqual(2, created.Count);
            Assert.AreEqual(inventory.Describe(), created[0].Describe());
            Assert.AreEqual(inventory.Describe(), created[1].Describe());
            CollectionAssert.AreEqual(new[] { "items Add 0 - 1:10", "items Add 1 - 2:20" }, created[0].Changes);
            CollectionAssert.AreEqual(new[] { "items Add 1 - 2:20" }, created[1].Changes);
        }

        [Test]
        public void OperationsOfAnUnobservedObjectAreDroppedAndTheNextSpawnCarriesTheContent()
        {
            var rule = new PeerRule();
            Connect();
            server.ServerManager.ObserverRule = rule;
            var inventory = new Inventory();
            Spawn(inventory);
            Run(5);

            inventory.Items.Add(Item(1, 10));
            Run(5);
            Assert.AreEqual(0, created.Count);

            rule.Allowed.Add(PeerIdOf(clients[0]));
            server.ServerManager.RebuildObservers();
            Run(5);
            inventory.Items.Add(Item(2, 20));
            Run(5);

            Assert.AreEqual(inventory.Describe(), created.Single().Describe());
            CollectionAssert.AreEqual(new[] { "items Add 1 - 2:20" }, created.Single().Changes);
        }

        [Test]
        public void AnOperationTheClientCannotApplyIsRepairedWithTheFullContentAndAReset()
        {
            Connect();
            var inventory = new Inventory();
            inventory.Items.Add(Item(1, 10));
            inventory.Items.Add(Item(2, 20));
            Spawn(inventory);
            Run(5);
            Inventory onClient = created.Single();
            onClient.Items.Items.RemoveAt(1);

            inventory.Items[1] = Item(3, 30);
            Run(10);

            Assert.AreEqual(inventory.Describe(), onClient.Describe());
            CollectionAssert.AreEqual(new[] { "items Reset -1 - -" }, onClient.Changes);

            inventory.Items.Add(Item(4, 40));
            Run(5);

            Assert.AreEqual(inventory.Describe(), onClient.Describe());
            CollectionAssert.AreEqual(new[] { "items Reset -1 - -", "items Add 2 - 4:40" }, onClient.Changes);
        }

        [Test]
        public void OperationsAlreadyInTheResyncContentAreSkipped()
        {
            Connect();
            var inventory = new Inventory();
            inventory.Items.Add(Item(1, 10));
            Spawn(inventory);
            Run(5);
            Inventory onClient = created.Single();
            onClient.Items.Items.Clear();

            inventory.Items[0] = Item(2, 20);
            for (int frame = 0; frame < 20 && !onClient.Items.Slot.OutOfSync; frame++)
            {
                Run(1);
            }

            Assert.IsTrue(onClient.Items.Slot.OutOfSync);
            inventory.Items.Add(Item(3, 30));
            Run(10);

            Assert.AreEqual(inventory.Describe(), onClient.Describe());
            CollectionAssert.AreEqual(new[] { "items Reset -1 - -" }, onClient.Changes);
        }

        [Test]
        public void ChangingASpawnedCollectionOnTheClientThrows()
        {
            Connect();
            var inventory = new Inventory();
            inventory.Items.Add(Item(1, 10));
            inventory.Slots[3] = Item(9, 90);
            Spawn(inventory);
            Run(5);
            Inventory onClient = created.Single();

            Assert.Throws<InvalidOperationException>(() => onClient.Items.Add(Item(2, 20)));
            Assert.Throws<InvalidOperationException>(() => onClient.Items.Insert(0, Item(2, 20)));
            Assert.Throws<InvalidOperationException>(() => onClient.Items[0] = Item(2, 20));
            Assert.Throws<InvalidOperationException>(() => onClient.Items.RemoveAt(0));
            Assert.Throws<InvalidOperationException>(() => onClient.Items.Remove(onClient.Items[0]));
            Assert.Throws<InvalidOperationException>(() => onClient.Items.Clear());
            Assert.Throws<InvalidOperationException>(() => onClient.Items.Dirty(0));
            Assert.Throws<InvalidOperationException>(() => onClient.Items.Modify(0, item => item.OwnerId = 1));
            Assert.Throws<InvalidOperationException>(() => onClient.Scores["a"] = 1);
            Assert.Throws<InvalidOperationException>(() => onClient.Scores.Add("a", 1));
            Assert.Throws<InvalidOperationException>(() => onClient.Scores.Remove("a"));
            Assert.Throws<InvalidOperationException>(() => onClient.Scores.Clear());
            Assert.Throws<InvalidOperationException>(() => onClient.Slots.Dirty(3));
            Assert.Throws<InvalidOperationException>(() => onClient.Slots.Modify(3, item => item.OwnerId = 1));
            Assert.Throws<InvalidOperationException>(() => onClient.Tags.Add(1));
            Assert.Throws<InvalidOperationException>(() => onClient.Tags.Remove(1));
            Assert.Throws<InvalidOperationException>(() => onClient.Tags.Clear());
            Assert.AreEqual(inventory.Describe(), onClient.Describe());
        }

        [Test]
        public void AnInstanceThatIsNotSpawnedChangesWithoutSendingOperations()
        {
            Connect();
            var inventory = new Inventory();
            Spawn(inventory);
            Run(5);
            server.ServerManager.Despawn(inventory.Entity);
            Run(5);

            inventory.Items.Add(Item(1, 10));
            inventory.Scores["a"] = 1;
            inventory.Tags.Add(7);
            server.ServerManager.Spawn(inventory.Entity);
            Run(5);

            Assert.AreEqual(2, created.Count);
            Assert.AreEqual(inventory.Describe(), created[1].StartedWith);
            CollectionAssert.IsEmpty(created[1].Changes);
        }

        [Test]
        public void AnObjectSpawnedAgainWithOperationsStillWaitingSendsItsLaterOperations()
        {
            Connect();
            var inventory = new Inventory();
            Spawn(inventory);
            Run(5);

            inventory.Items.Add(Item(1, 10));
            server.ServerManager.Despawn(inventory.Entity);
            server.ServerManager.Spawn(inventory.Entity);
            Run(5);
            inventory.Items.Add(Item(2, 20));
            Run(5);

            Assert.AreEqual(inventory.Describe(), created[1].Describe());
            CollectionAssert.AreEqual(new[] { "items Add 1 - 2:20" }, created[1].Changes);
        }

        [Test]
        public void AChangesMessageThatCannotBeReadResyncsEveryCollectionOfTheBehaviour()
        {
            Connect();
            var inventory = new Inventory();
            inventory.Items.Add(Item(1, 10));
            inventory.Tags.Add(7);
            Spawn(inventory);
            Run(5);
            Inventory onClient = created.Single();
            onClient.Items.Items.Clear();
            onClient.Tags.Items.Clear();

            server.ServerManager.Objects.SendToObservers(TestObjects.CollectionChangesId, inventory.Entity.Record.ObjectId, 0, new byte[] { 2, 1 });
            Run(10);

            Assert.AreEqual(inventory.Describe(), onClient.Describe());
            Assert.AreEqual(4, onClient.Changes.Count(change => change.Contains(" Reset ")));
        }

        [Test]
        public void ChangesWhoseKindsDoNotMatchTheirValuesResyncThatCollection()
        {
            Connect();
            var inventory = new Inventory();
            inventory.Items.Add(Item(1, 10));
            Spawn(inventory);
            Run(5);
            Inventory onClient = created.Single();
            var malformed = new CollectionChanges { Collection = 2, First = 100, Kinds = new byte[] { 1 } };

            server.ServerManager.Objects.SendToObservers(TestObjects.CollectionChangesId, inventory.Entity.Record.ObjectId, 0, CollectionChangesNetAdapter.Instance.Encode(malformed).Span);
            Run(10);

            Assert.AreEqual(inventory.Describe(), onClient.Describe());
            CollectionAssert.AreEqual(new[] { "items Reset -1 - -" }, onClient.Changes);
        }

        [Test]
        public void AResyncFromAPeerOutsideTheSetGetsNoContent()
        {
            var log = new NetworkLog(logged.Add, message => { });
            var serverDispatcher = new MessageDispatcher(TestObjects.Schema());
            var serverSession = new ServerSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), serverDispatcher, TestBundles.Protocol(TestObjects.Channels()));
            ServerEntities entities = null;
            var objects = new ServerObjects(serverSession, TestObjects.Protocol(TestObjects.Channels()), objectId => entities.ReadSpawnData(objectId));
            var inputs = new ServerInputs(serverSession, objects, TestObjects.InputProtocol(), new ServerClock(serverSession, TestObjects.ClockProtocol()));
            entities = new ServerEntities(this, serverSession, objects, inputs, serverDispatcher, TestObjects.Channels(), TestObjects.StateProtocol(TestObjects.Channels()), TestObjects.InputProtocol(), TestObjects.TransformProtocol(TestObjects.Channels()), new RpcMessageIds(), new Fomoxa.Networking.Tests.ClientEntitiesTest.FakeServerBackend(), log);
            var rule = new SwitchRule();
            entities.ObserverRule = rule;
            var listener = new LoopbackListener(8);
            serverSession.Start(listener);
            var clientDispatcher = new MessageDispatcher(TestObjects.Schema());
            var received = new List<uint>();
            clientDispatcher.Register(TestObjects.LocalPeerId, (peerId, payload) => { });
            clientDispatcher.RegisterObject(TestObjects.CollectionContentId, (peerId, objectId, behaviourIndex, body) => received.Add(objectId));
            var clientSession = new ClientSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), clientDispatcher, TestBundles.Protocol(TestObjects.Channels()));
            clientSession.SetAcceptance(TestObjects.LocalPeerId);
            clientSession.Start(listener.Connect(), now);
            Tick(serverSession, clientSession, 20);
            var listed = new Listed();
            entities.Spawn(new Fomoxa.Networking.Tests.ClientEntitiesTest.FakeEntity(0xA1, listed), 0);
            uint spawnedId = listed.Entity.Record.ObjectId;
            byte[] request = CollectionResyncNetAdapter.Instance.Encode(new CollectionResync { Collection = 3 }).ToArray();

            clientSession.SendToObject(TestObjects.CollectionResyncId, spawnedId, 0, request);
            Tick(serverSession, clientSession, 10);

            Assert.IsEmpty(received);

            rule.Open = true;
            objects.RebuildObservers();
            clientSession.SendToObject(TestObjects.CollectionResyncId, spawnedId, 0, request);
            Tick(serverSession, clientSession, 10);

            CollectionAssert.AreEqual(new[] { spawnedId }, received);
        }

        [Test]
        public void ACollectionIndexUsedTwiceIsRefused()
        {
            Connect();

            Assert.Throws<HandlerRegistrationException>(() => Spawn(new Registering(state =>
            {
                state.Use(1, new NetworkList<int>());
                state.Use(1, new NetworkSet<int>());
            })));
        }

        [Test]
        public void ACollectionInstanceUsedTwiceIsRefused()
        {
            Connect();
            var list = new NetworkList<int>();

            Assert.Throws<HandlerRegistrationException>(() => Spawn(new Registering(state =>
            {
                state.Use(1, list);
                state.Use(2, list);
            })));
        }

        [Test]
        public void AModelWithoutItsCodecOrAsAKeyIsRefused()
        {
            Connect();

            Assert.Throws<HandlerRegistrationException>(() => Spawn(new Registering(state => state.Use(1, new NetworkList<ObjectOwnerChange>()))));
            Assert.Throws<HandlerRegistrationException>(() => Spawn(new Registering(state => state.Use(1, new NetworkDictionary<int, ObjectOwnerChange>()))));
            Assert.Throws<HandlerRegistrationException>(() => Spawn(new Registering(state => state.Use(1, new NetworkSet<ObjectOwnerChange>()))));
            Assert.Throws<HandlerRegistrationException>(() => Spawn(new Registering(state => state.Use(1, new NetworkDictionary<ObjectOwnerChange, int>()))));
            Assert.Throws<HandlerRegistrationException>(() => Spawn(new Registering(state => state.Use(1, new NetworkDictionary<ObjectOwnerChange, ObjectOwnerChange>(), ObjectOwnerChangeNetAdapter.Instance))));
        }

        [Test]
        public void EveryPrimitiveTypeCanBeAnElement()
        {
            Connect();
            var primitives = new Primitives();
            primitives.Fill();
            serverPrefabs.Register(PrefabId + 1, Fingerprint, () => new StandaloneEntity(PrefabId + 1, new EntityBehaviour[] { new Primitives() }));
            clientPrefabs.Register(PrefabId + 1, Fingerprint, () => new StandaloneEntity(PrefabId + 1, new EntityBehaviour[] { new Primitives() }));

            server.ServerManager.Spawn(new StandaloneEntity(PrefabId + 1, new EntityBehaviour[] { primitives }));
            Run(5);

            var onClient = (Primitives)clients[0].ClientManager.Spawned.Values.Single().EntityBehaviours[0];
            Assert.AreEqual(primitives.Describe(), onClient.Describe());
        }

        [Test]
        public void NullElementsValuesAndKeysThrowAtTheCall()
        {
            var list = new NetworkList<string>();
            var dictionary = new NetworkDictionary<string, ObjectOwnerChange>();
            var set = new NetworkSet<string>();
            list.Add("x");

            Assert.Throws<ArgumentNullException>(() => list.Add(null));
            Assert.Throws<ArgumentNullException>(() => list.Insert(0, null));
            Assert.Throws<ArgumentNullException>(() => list[0] = null);
            Assert.Throws<ArgumentNullException>(() => dictionary.Add(null, Item(1, 1)));
            Assert.Throws<ArgumentNullException>(() => dictionary.Add("a", null));
            Assert.Throws<ArgumentNullException>(() => dictionary["a"] = null);
            Assert.Throws<ArgumentNullException>(() => dictionary[null] = Item(1, 1));
            Assert.Throws<ArgumentNullException>(() => set.Add(null));
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(0, dictionary.Count);
            Assert.AreEqual(0, set.Count);
        }

        [Test]
        public void ASpawnWhoseCollectionsDoNotMatchTheClientIsInvalid()
        {
            clientPrefabs.Register(PrefabId + 2, Fingerprint, () => new StandaloneEntity(PrefabId + 2, new EntityBehaviour[] { new Registering(state => state.Use(4, new NetworkList<int>())) }));
            Connect();

            server.ServerManager.Spawn(new StandaloneEntity(PrefabId + 2, new EntityBehaviour[] { new Registering(state => state.Use(4, new NetworkList<uint>())) }));
            Run(5);

            Assert.AreEqual(ObjectMismatchKind.InvalidSpawn, mismatches.Single().Kind);
        }

        [Test]
        public void TheStateOfABehaviourReachesTheClientBeforeItsCollections()
        {
            var order = new List<string>();
            clientPrefabs.Register(PrefabId + 3, Fingerprint, () => new StandaloneEntity(PrefabId + 3, new EntityBehaviour[] { new Leveled(order) }));
            Connect();
            var leveled = new Leveled(null);
            server.ServerManager.Spawn(new StandaloneEntity(PrefabId + 3, new EntityBehaviour[] { leveled }));
            Run(5);

            leveled.Rewards.Add(5);
            leveled.Level.Value = 2;
            Run(5);

            CollectionAssert.AreEqual(new[] { "level 2", "rewards Add 5" }, order);
        }

        [Test]
        public void TheLocalClientOfAHostReportsChangesFromItsOwnCopy()
        {
            Connect(0);
            server.ClientManager.StartConnection("127.0.0.1", 7777);
            Run(10);
            var inventory = new Inventory();
            inventory.Items.Add(Item(1, 10));
            Spawn(inventory);
            Run(5);

            inventory.Items.Add(Item(2, 20));
            inventory.Items[0] = Item(3, 30);
            Run(5);

            Assert.AreEqual("items [3:30,2:20] scores [] slots [] tags []", inventory.Describe());
            CollectionAssert.AreEqual(new[] { "items Add 1 - 2:20", "items Set 0 1:10 3:30" }, inventory.Changes);
        }

        [Test]
        public void OneOperationOnALongListSendsOnlyThatOperation()
        {
            var changes = new CollectionChanges { Collection = 2, First = 501, Kinds = new byte[] { 1 } };
            changes.Values.Add(I32ValueNetAdapter.Instance.Encode(new I32Value { Value = 7 }).ToArray());

            Assert.AreEqual(1 + 4 + (4 + 1) + 4 + 4 + 4 + (4 + 4), CollectionChangesNetAdapter.Instance.Encode(changes).Length);
        }

        private static ObjectOwnerChange Item(uint id, ulong value) => new ObjectOwnerChange { ObjectId = id, OwnerId = value };

        private static string Text(ObjectOwnerChange item) => item == null ? "-" : $"{item.ObjectId}:{item.OwnerId}";

        private static ulong PeerIdOf(NetworkRuntime client) => client.ClientManager.Objects.LocalPeerId;

        private void Spawn(EntityBehaviour behaviour)
        {
            var entity = new StandaloneEntity(PrefabId, new[] { behaviour });
            if (behaviour is Inventory inventory)
            {
                inventory.Entity = entity;
            }

            server.ServerManager.Spawn(entity);
        }

        private void Tick(ServerSession serverSession, ClientSession clientSession, int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                serverSession.Tick(now);
                clientSession.Tick(now);
                serverSession.Flush();
                clientSession.Flush();
            }
        }

        private void Connect(int clientCount = 1)
        {
            var log = new NetworkLog(logged.Add, message => { });
            var files = new StandaloneBackendTest.MemorySceneFiles();
            server = StandaloneRuntime.Create(registry, new NetworkSettings(), factory, serverPrefabs, new StandaloneBehaviours(), files, () => now, log);
            server.ServerManager.StartConnection(7777);
            for (int index = 0; index < clientCount; index++)
            {
                NetworkRuntime client = StandaloneRuntime.Create(registry, new NetworkSettings(), factory, clientPrefabs, new StandaloneBehaviours(), files, () => now, log);
                client.ClientManager.OnObjectMismatch += mismatches.Add;
                client.ClientManager.StartConnection("127.0.0.1", 7777);
                clients.Add(client);
            }

            Run(10);
            foreach (NetworkRuntime client in clients)
            {
                Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            }
        }

        private void Run(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                server.BeginFrame(now, FrameSeconds);
                foreach (NetworkRuntime client in clients)
                {
                    client.BeginFrame(now, FrameSeconds);
                }

                server.EndFrame();
                foreach (NetworkRuntime client in clients)
                {
                    client.EndFrame();
                }
            }
        }

        private sealed class PeerRule : IObserverRule
        {
            public HashSet<ulong> Allowed { get; } = new HashSet<ulong>();

            public bool RebuildsOnFirstAnchor => false;

            public bool RebuildsPeriodically => false;

            public bool Observes(ObserverContext context, INetworkEntity entity, ulong peerId) => Allowed.Contains(peerId);
        }

        private sealed class SwitchRule : IObserverRule
        {
            public bool Open { get; set; }

            public bool RebuildsOnFirstAnchor => false;

            public bool RebuildsPeriodically => false;

            public bool Observes(ObserverContext context, INetworkEntity entity, ulong peerId) => Open;
        }

        private sealed class Listed : Fomoxa.Networking.Tests.ClientEntitiesTest.RecordingBehaviour
        {
            public Listed()
                : base("listed", new List<string>())
            {
            }

            public NetworkList<int> Values { get; } = new NetworkList<int>();

            public INetworkEntity Entity => Values.Slot.Record.Representation;

            protected override void OnRegisterState(NetworkState state) => state.Use(3, Values);
        }

        private sealed class Inventory : EntityBehaviour
        {
            public NetworkList<ObjectOwnerChange> Items { get; } = new NetworkList<ObjectOwnerChange>();

            public NetworkDictionary<string, int> Scores { get; } = new NetworkDictionary<string, int>();

            public NetworkDictionary<int, ObjectOwnerChange> Slots { get; } = new NetworkDictionary<int, ObjectOwnerChange>();

            public NetworkSet<uint> Tags { get; } = new NetworkSet<uint>();

            public List<string> Changes { get; } = new List<string>();

            public string StartedWith { get; private set; }

            public StandaloneEntity Entity { get; set; }

            public override void OnStartClient() => StartedWith = Describe();

            public string Describe() =>
                $"items [{string.Join(",", Items.Select(Text))}] scores [{string.Join(",", Scores.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Value}"))}] slots [{string.Join(",", Slots.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={Text(entry.Value)}"))}] tags [{string.Join(",", Tags.OrderBy(tag => tag))}]";

            protected override void OnRegisterState(NetworkState state)
            {
                state.Use(5, Tags, (operation, oldItem, newItem) => Changes.Add($"tags {operation} {oldItem} {newItem}"));
                state.Use(2, Items, ObjectOwnerChangeNetAdapter.Instance, (operation, index, oldItem, newItem) => Changes.Add($"items {operation} {index} {Text(oldItem)} {Text(newItem)}"));
                state.Use(0, Scores, (operation, key, oldValue, newValue) => Changes.Add($"scores {operation} {key} {oldValue} {newValue}"));
                state.Use(7, Slots, ObjectOwnerChangeNetAdapter.Instance, (operation, key, oldValue, newValue) => Changes.Add($"slots {operation} {key} {Text(oldValue)} {Text(newValue)}"));
            }
        }

        private sealed class Registering : EntityBehaviour
        {
            private readonly Action<NetworkState> register;

            public Registering(Action<NetworkState> register)
            {
                this.register = register;
            }

            protected override void OnRegisterState(NetworkState state) => register(state);
        }

        private sealed class Primitives : EntityBehaviour
        {
            private readonly NetworkList<bool> bools = new NetworkList<bool>();
            private readonly NetworkList<sbyte> sbytes = new NetworkList<sbyte>();
            private readonly NetworkList<byte> bytes = new NetworkList<byte>();
            private readonly NetworkList<short> shorts = new NetworkList<short>();
            private readonly NetworkList<ushort> ushorts = new NetworkList<ushort>();
            private readonly NetworkList<int> ints = new NetworkList<int>();
            private readonly NetworkList<uint> uints = new NetworkList<uint>();
            private readonly NetworkList<long> longs = new NetworkList<long>();
            private readonly NetworkList<ulong> ulongs = new NetworkList<ulong>();
            private readonly NetworkList<float> floats = new NetworkList<float>();
            private readonly NetworkList<double> doubles = new NetworkList<double>();
            private readonly NetworkDictionary<string, string> strings = new NetworkDictionary<string, string>();

            public void Fill()
            {
                bools.Add(true);
                sbytes.Add(-8);
                bytes.Add(8);
                shorts.Add(-16);
                ushorts.Add(16);
                ints.Add(-32);
                uints.Add(32);
                longs.Add(-64);
                ulongs.Add(64);
                floats.Add(1.5f);
                doubles.Add(2.25);
                strings["key"] = "value";
            }

            public string Describe() =>
                string.Join(" ", bools.Single(), sbytes.Single(), bytes.Single(), shorts.Single(), ushorts.Single(), ints.Single(), uints.Single(), longs.Single(), ulongs.Single(), floats.Single(), doubles.Single(), strings.Single());

            protected override void OnRegisterState(NetworkState state)
            {
                state.Use(0, bools);
                state.Use(1, sbytes);
                state.Use(2, bytes);
                state.Use(3, shorts);
                state.Use(4, ushorts);
                state.Use(5, ints);
                state.Use(6, uints);
                state.Use(7, longs);
                state.Use(8, ulongs);
                state.Use(9, floats);
                state.Use(10, doubles);
                state.Use(11, strings);
            }
        }

        private sealed class Leveled : EntityBehaviour
        {
            private readonly List<string> order;

            public Leveled(List<string> order)
            {
                this.order = order;
            }

            public Level Level { get; } = new Level();

            public NetworkList<int> Rewards { get; } = new NetworkList<int>();

            protected override void OnRegisterState(NetworkState state)
            {
                state.Use(0, Rewards, (operation, index, oldItem, newItem) => order?.Add($"rewards {operation} {newItem}"));
                state.Use(LevelCodec.Instance, Level, previous => order?.Add($"level {Level.Value}"));
            }
        }

        private sealed class Level
        {
            public int Value { get; set; }
        }

        private sealed class LevelCodec : IMessageCodec<Level>
        {
            public static readonly LevelCodec Instance = new LevelCodec();

            private readonly byte[] buffer = new byte[sizeof(int)];

            public uint MessageId => 0x2000_0020;

            public ReadOnlyMemory<byte> Encode(Level value)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer, value.Value);
                return buffer;
            }

            public void Decode(ReadOnlyMemory<byte> payload, ref Level value)
            {
                value ??= new Level();
                value.Value = BinaryPrimitives.ReadInt32LittleEndian(payload.Span);
            }
        }
    }
}
