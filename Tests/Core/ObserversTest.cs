using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ObserversTest
    {
        private const uint PrefabId = 0xA1;
        private const uint Fingerprint = 0xF1;
        private const ulong SceneObjectId = 0xABCD_0001_0000_0007;

        [Test]
        public void WithoutARuleEveryStartedPeerObservesEveryObject()
        {
            var host = new Host();
            Client first = host.Connect();
            Client second = host.Connect();
            host.Run(20);

            uint objectId = host.Objects.Spawn(PrefabId, 0);
            host.Run(10);

            CollectionAssert.AreEquivalent(host.StartedPeers, host.Objects.ObserversOf(objectId));
            Assert.AreEqual(1, first.Spawner.Spawned.Count);
            Assert.AreEqual(1, second.Spawner.Spawned.Count);
            Assert.AreEqual(2, host.Objects.SendToObservers(TestObjects.GreetingId, objectId, 0, new byte[] { 1 }));
        }

        [Test]
        public void TheRuleChoosesWhoGetsTheSpawnAndTheOwnerAlwaysObserves()
        {
            var host = new Host();
            Client owner = host.Connect();
            Client other = host.Connect();
            host.Run(20);
            host.Hidden.Add(owner.PeerId(host));
            host.Hidden.Add(other.PeerId(host));
            host.Objects.Observes = host.Decide;

            uint objectId = host.Objects.Spawn(PrefabId, owner.PeerId(host));
            Assert.AreEqual(1, host.Objects.SendToObservers(TestObjects.GreetingId, objectId, 0, new byte[] { 7 }));
            Assert.AreEqual(SendResult.NotObserver, host.Objects.SendToObserver(other.PeerId(host), TestObjects.GreetingId, objectId, 0, new byte[] { 8 }));
            Assert.AreEqual(SendResult.Queued, host.Objects.SendToObserver(owner.PeerId(host), TestObjects.GreetingId, objectId, 0, new byte[] { 9 }));
            host.Run(10);

            CollectionAssert.AreEqual(new[] { owner.PeerId(host) }, host.Objects.ObserversOf(objectId));
            Assert.AreEqual(1, owner.Spawner.Spawned.Count);
            Assert.AreEqual(0, other.Spawner.Spawned.Count);
            CollectionAssert.AreEqual(new byte[] { 7, 9 }, owner.Received.Select(body => body[0]));
            Assert.AreEqual(0, other.Received.Count);
            Assert.IsFalse(host.Objects.IsObserver(objectId, other.PeerId(host)));
        }

        [Test]
        public void RebuildDespawnsForAPeerThatLeavesAndSpawnsTheCurrentStateForAPeerThatEnters()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            host.Objects.Observes = host.Decide;
            uint objectId = host.Objects.Spawn(PrefabId, 0);
            host.Run(10);

            host.Hidden.Add(client.PeerId(host));
            Assert.IsTrue(host.Objects.RebuildObservers(objectId));
            host.Run(10);

            CollectionAssert.AreEqual(new[] { objectId }, client.Spawner.Despawned);
            Assert.AreEqual(0, client.Objects.Count);

            host.Hidden.Clear();
            host.Transforms[objectId] = new Vector3(4, 5, 6);
            host.States[objectId] = new[] { new byte[] { 3, 1 } };
            var added = new List<ObserverAddedArgs>();
            host.Objects.OnObserverAdded += added.Add;
            host.Objects.RebuildObservers(objectId);
            host.Run(10);

            Assert.AreEqual(2, client.Spawner.Spawned.Count);
            Assert.AreEqual(new Vector3(4, 5, 6), client.Spawner.Spawned[1].Position);
            CollectionAssert.AreEqual(new byte[] { 3, 1 }, client.Spawner.States[1][0]);
            Assert.AreEqual(1, added.Count);
            Assert.AreEqual(objectId, added[0].ObjectId);
            Assert.AreEqual(client.PeerId(host), added[0].PeerId);
        }

        [Test]
        public void SceneObjectsLeaveAndEnterWithTheSceneSpawn()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            host.Objects.Observes = host.Decide;
            uint objectId = host.Objects.SpawnScene(SceneObjectId, 0);
            host.Hidden.Add(client.PeerId(host));
            host.Objects.RebuildObservers();
            host.Hidden.Clear();
            host.Objects.RebuildObservers();
            host.Run(10);

            Assert.AreEqual(2, client.Spawner.Spawned.Count);
            Assert.AreEqual(SceneObjectId, client.Spawner.Spawned[1].SceneObjectId);
            CollectionAssert.AreEqual(new[] { objectId }, client.Spawner.Despawned);
            Assert.AreEqual(0, client.Mismatches.Count);
        }

        [Test]
        public void TheRoundRebuildsEveryObjectWithinTheInterval()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            host.Objects.Observes = host.Decide;
            host.Objects.ObserverInterval = 3;
            var objectIds = new List<uint>();
            for (int index = 0; index < 6; index++)
            {
                objectIds.Add(host.Objects.Spawn(PrefabId, 0));
            }

            host.Hidden.Add(client.PeerId(host));
            host.Objects.RebuildObserversRound();
            Assert.AreEqual(2, objectIds.Count(objectId => !host.Objects.IsObserver(objectId, client.PeerId(host))));
            host.Objects.RebuildObserversRound();
            Assert.AreEqual(4, objectIds.Count(objectId => !host.Objects.IsObserver(objectId, client.PeerId(host))));
            host.Objects.RebuildObserversRound();
            Assert.AreEqual(6, objectIds.Count(objectId => !host.Objects.IsObserver(objectId, client.PeerId(host))));
            host.Run(10);

            CollectionAssert.AreEqual(objectIds, client.Spawner.Despawned);
        }

        [Test]
        public void TheRoundRebuildsAnObjectOncePerInterval()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            host.Objects.Observes = host.Decide;
            host.Objects.ObserverInterval = 3;
            uint objectId = host.Objects.Spawn(PrefabId, 0);
            host.Hidden.Add(client.PeerId(host));

            host.Objects.RebuildObserversRound();
            Assert.IsFalse(host.Objects.IsObserver(objectId, client.PeerId(host)));
            host.Hidden.Clear();
            host.Objects.RebuildObserversRound();
            host.Objects.RebuildObserversRound();
            Assert.IsFalse(host.Objects.IsObserver(objectId, client.PeerId(host)));
            host.Objects.RebuildObserversRound();
            Assert.IsTrue(host.Objects.IsObserver(objectId, client.PeerId(host)));
        }

        [Test]
        public void RebuildingOnePeerLeavesTheOtherPeersAlone()
        {
            var host = new Host();
            Client first = host.Connect();
            Client second = host.Connect();
            host.Run(20);
            host.Objects.Observes = host.Decide;
            uint objectId = host.Objects.Spawn(PrefabId, 0);
            host.Hidden.Add(first.PeerId(host));
            host.Hidden.Add(second.PeerId(host));

            host.Objects.RebuildObserversOfPeer(first.PeerId(host));

            CollectionAssert.AreEqual(new[] { second.PeerId(host) }, host.Objects.ObserversOf(objectId));
        }

        [Test]
        public void ANewOwnerOutsideTheSetGetsTheSpawnAndAnOldOwnerTheRuleHidesGetsTheDespawn()
        {
            var host = new Host();
            Client oldOwner = host.Connect();
            Client newOwner = host.Connect();
            Client watcher = host.Connect();
            host.Run(20);
            host.Objects.Observes = host.Decide;
            host.Hidden.Add(oldOwner.PeerId(host));
            host.Hidden.Add(newOwner.PeerId(host));
            uint objectId = host.Objects.Spawn(PrefabId, oldOwner.PeerId(host));
            host.Run(10);

            Assert.IsTrue(host.Objects.ChangeOwner(objectId, newOwner.PeerId(host)));
            host.Run(10);

            CollectionAssert.AreEquivalent(new[] { newOwner.PeerId(host), watcher.PeerId(host) }, host.Objects.ObserversOf(objectId));
            Assert.AreEqual(0, oldOwner.OwnerChanges.Count);
            CollectionAssert.AreEqual(new[] { objectId }, oldOwner.Spawner.Despawned);
            Assert.AreEqual(0, newOwner.OwnerChanges.Count);
            Assert.AreEqual(1, newOwner.Spawner.Spawned.Count);
            Assert.AreEqual(newOwner.PeerId(host), newOwner.Spawner.Spawned[0].OwnerId);
            Assert.IsTrue(newOwner.Objects.IsOwner(objectId));
            Assert.AreEqual(1, watcher.OwnerChanges.Count);
            Assert.AreEqual(newOwner.PeerId(host), watcher.OwnerChanges[0].OwnerId);
        }

        [Test]
        public void AnOldOwnerTheRuleStillShowsGetsTheOwnerChange()
        {
            var host = new Host();
            Client oldOwner = host.Connect();
            Client newOwner = host.Connect();
            host.Run(20);
            host.Objects.Observes = host.Decide;
            uint objectId = host.Objects.Spawn(PrefabId, oldOwner.PeerId(host));
            host.Run(10);

            host.Objects.ChangeOwner(objectId, newOwner.PeerId(host));
            host.Run(10);

            Assert.AreEqual(1, oldOwner.OwnerChanges.Count);
            Assert.AreEqual(1, newOwner.OwnerChanges.Count);
            Assert.AreEqual(0, oldOwner.Spawner.Despawned.Count);
            Assert.AreEqual(1, newOwner.Spawner.Spawned.Count);
        }

        [Test]
        public void ALatePeerGetsOnlyTheObjectsItObservesAndAnAddedEventForEach()
        {
            var host = new Host();
            host.Objects.Observes = host.Decide;
            uint shown = host.Objects.Spawn(PrefabId, 0);
            uint hidden = host.Objects.Spawn(PrefabId, 0);
            host.HiddenObjects.Add(hidden);
            var added = new List<ObserverAddedArgs>();
            host.Objects.OnObserverAdded += added.Add;

            Client client = host.Connect();
            host.Run(20);

            Assert.AreEqual(1, client.Spawner.Spawned.Count);
            Assert.AreEqual(shown, client.Spawner.Spawned[0].ObjectId);
            Assert.AreEqual(1, added.Count);
            Assert.AreEqual(shown, added[0].ObjectId);
            Assert.IsFalse(host.Objects.IsObserver(hidden, client.PeerId(host)));
        }

        [Test]
        public void APeerThatStopsLeavesEverySet()
        {
            var host = new Host();
            Client leaving = host.Connect();
            Client staying = host.Connect();
            host.Run(20);
            ulong leavingId = leaving.PeerId(host);
            uint objectId = host.Objects.Spawn(PrefabId, 0);

            host.Server.Disconnect(leavingId);

            CollectionAssert.AreEqual(new[] { staying.PeerId(host) }, host.Objects.ObserversOf(objectId));
            Assert.AreEqual(SendResult.NotConnected, host.Objects.SendToObserver(leavingId, TestObjects.GreetingId, objectId, 0, new byte[] { 1 }));
        }

        [Test]
        public void ASpawnSourceThatFailsOnEntryStopsThatPeerWithAHandlerException()
        {
            var host = new Host();
            Client failing = host.Connect();
            Client other = host.Connect();
            host.Run(20);
            ulong failingId = failing.PeerId(host);
            host.Objects.Observes = host.Decide;
            host.Hidden.Add(failingId);
            uint objectId = host.Objects.Spawn(PrefabId, 0);

            host.Hidden.Clear();
            host.Failing.Add(objectId);
            host.Objects.RebuildObservers(objectId);

            Assert.AreEqual(1, host.Exceptions.Count);
            Assert.AreEqual(failingId, host.Exceptions[0].PeerId);
            Assert.AreEqual(TestObjects.SpawnId, host.Exceptions[0].MessageId);
            Assert.AreEqual(StopReason.HandlerException, host.Stopped.Single().Reason);
            CollectionAssert.AreEqual(new[] { other.PeerId(host) }, host.Objects.ObserversOf(objectId));
        }

        [Test]
        public void TheRuleCannotChangeObjectsWhileItDecides()
        {
            var host = new Host();
            host.Connect();
            host.Run(20);
            uint objectId = host.Objects.Spawn(PrefabId, 0);
            var attempts = new List<Func<bool>>
            {
                () => host.Objects.Despawn(objectId),
                () => host.Objects.ChangeOwner(objectId, 0),
                () => host.Objects.Spawn(PrefabId, 0) != 0,
                () => host.Objects.RebuildObservers(objectId),
            };

            foreach (Func<bool> attempt in attempts)
            {
                host.Objects.Observes = (candidate, peerId) => attempt();
                Assert.Throws<InvalidOperationException>(() => host.Objects.RebuildObservers(objectId));
            }

            Assert.AreEqual(1, host.Objects.Count);
            Assert.IsTrue(host.Objects.TryGet(objectId, out ObjectRow row));
            Assert.AreEqual(0UL, row.OwnerId);
            host.Objects.Observes = (candidate, peerId) => true;
            Assert.IsTrue(host.Objects.RebuildObservers(objectId));
        }

        [Test]
        public void AnExceptionFromTheRuleInChangeOwnerKeepsTheOldOwner()
        {
            var host = new Host();
            Client oldOwner = host.Connect();
            Client newOwner = host.Connect();
            host.Run(20);
            uint objectId = host.Objects.Spawn(PrefabId, oldOwner.PeerId(host));
            host.Run(10);
            var changes = new List<ObjectOwnerChangedArgs>();
            host.Objects.OnOwnerChanged += changes.Add;
            host.Objects.Observes = (candidate, peerId) => throw new InvalidOperationException("rule failed");

            Assert.Throws<InvalidOperationException>(() => host.Objects.ChangeOwner(objectId, newOwner.PeerId(host)));
            host.Run(10);

            Assert.IsTrue(host.Objects.TryGet(objectId, out ObjectRow row));
            Assert.AreEqual(oldOwner.PeerId(host), row.OwnerId);
            Assert.AreEqual(0, changes.Count);
            Assert.AreEqual(0, oldOwner.OwnerChanges.Count);
            Assert.AreEqual(0, newOwner.OwnerChanges.Count);
            CollectionAssert.AreEquivalent(new[] { oldOwner.PeerId(host), newOwner.PeerId(host) }, host.Objects.ObserversOf(objectId));
        }

        [Test]
        public void AnExceptionFromTheRuleWhenAPeerStartsKeepsItsHeldMessagesFromTheNextPeer()
        {
            var host = new Host();
            uint objectId = host.Objects.Spawn(PrefabId, 0);
            ulong failingId = 0;
            host.Server.OnRemoteConnectionState += args =>
            {
                if (args.State == ConnectionState.Starting)
                {
                    host.Server.SendToObject(args.PeerId, TestObjects.GreetingId, objectId, 0, new byte[] { (byte)args.PeerId });
                }
            };
            host.Objects.Observes = (candidate, peerId) =>
            {
                failingId = peerId;
                throw new InvalidOperationException("rule failed");
            };
            Client failing = host.Connect();

            Assert.Throws<InvalidOperationException>(() => host.Run(20));

            Assert.AreNotEqual(0UL, failingId);
            Assert.IsFalse(host.Objects.IsObserver(objectId, failingId));
            host.Objects.Observes = (candidate, peerId) => peerId != failingId;
            Client next = host.Connect();
            host.Run(20);

            Assert.AreEqual(1, next.Spawner.Spawned.Count);
            Assert.AreEqual(1, next.Received.Count);
            Assert.AreNotEqual((byte)failingId, next.Received[0][0]);
        }

        [Test]
        public void AnExceptionFromTheRuleReachesTheCaller()
        {
            var host = new Host();
            host.Connect();
            host.Run(20);
            host.Objects.Observes = (objectId, peerId) => throw new InvalidOperationException("rule failed");

            Assert.Throws<InvalidOperationException>(() => host.Objects.Spawn(PrefabId, 0));
            Assert.AreEqual(0, host.Objects.Count);
        }

        [Test]
        public void TheIntervalIsAtLeastOneTick()
        {
            var host = new Host();

            Assert.Throws<ArgumentOutOfRangeException>(() => host.Objects.ObserverInterval = 0);
            Assert.AreEqual(15, host.Objects.ObserverInterval);
        }

        private sealed class Host
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly ServerSession Server;
            public readonly ServerObjects Objects;
            public readonly Dictionary<uint, Vector3> Transforms = new Dictionary<uint, Vector3>();
            public readonly Dictionary<uint, byte[][]> States = new Dictionary<uint, byte[][]>();
            public readonly HashSet<uint> Failing = new HashSet<uint>();
            public readonly HashSet<ulong> Hidden = new HashSet<ulong>();
            public readonly HashSet<uint> HiddenObjects = new HashSet<uint>();
            public readonly List<ulong> StartedPeers = new List<ulong>();
            public readonly List<ConnectionStateArgs> Stopped = new List<ConnectionStateArgs>();
            public readonly List<HandlerExceptionArgs> Exceptions = new List<HandlerExceptionArgs>();
            public readonly List<Client> Clients = new List<Client>();
            public TimeSpan Now = TimeSpan.Zero;

            public Host()
            {
                Server = new ServerSession(
                    TestObjects.Schema(),
                    new SessionConfig(),
                    new SessionLimits(),
                    new MessageDispatcher(TestObjects.Schema()),
                    TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ServerObjects(Server, TestObjects.Protocol(TestObjects.Channels()), Read);
                Server.OnHandlerException += Exceptions.Add;
                Server.OnRemoteConnectionState += args =>
                {
                    if (args.State == ConnectionState.Started)
                    {
                        StartedPeers.Add(args.PeerId);
                    }
                    else if (args.State == ConnectionState.Stopped)
                    {
                        Stopped.Add(args);
                    }
                };
                Server.Start(Listener);
            }

            public bool Decide(uint objectId, ulong peerId) => !Hidden.Contains(peerId) && !HiddenObjects.Contains(objectId);

            public Client Connect()
            {
                var client = new Client(this);
                Clients.Add(client);
                return client;
            }

            public void Run(int steps)
            {
                for (int step = 0; step < steps; step++)
                {
                    Now += TimeSpan.FromMilliseconds(16);
                    Server.Tick(Now);
                    foreach (Client client in Clients)
                    {
                        client.Session.Tick(Now);
                    }

                    Server.Flush();
                    foreach (Client client in Clients)
                    {
                        client.Session.Flush();
                    }
                }
            }

            private SpawnData Read(uint objectId)
            {
                if (Failing.Contains(objectId))
                {
                    throw new InvalidOperationException("spawn source failed");
                }

                Vector3 position = Transforms.TryGetValue(objectId, out Vector3 stored) ? stored : Vector3.Zero;
                ReadOnlyMemory<byte>[] states = States.TryGetValue(objectId, out byte[][] bytes)
                    ? bytes.Select(state => new ReadOnlyMemory<byte>(state)).ToArray()
                    : null;
                return new SpawnData(Fingerprint, position, Quaternion.Identity, Vector3.One, states);
            }
        }

        private sealed class Client
        {
            public readonly ClientSession Session;
            public readonly ClientObjects Objects;
            public readonly RecordingSpawner Spawner = new RecordingSpawner();
            public readonly List<ObjectMismatchArgs> Mismatches = new List<ObjectMismatchArgs>();
            public readonly List<ObjectOwnerChangedArgs> OwnerChanges = new List<ObjectOwnerChangedArgs>();
            public readonly List<byte[]> Received = new List<byte[]>();
            private readonly int index;

            public Client(Host host)
            {
                index = host.Clients.Count;
                var dispatcher = new MessageDispatcher(TestObjects.Schema());
                dispatcher.RegisterObject(TestObjects.GreetingId, (peerId, objectId, behaviourIndex, body) => Received.Add(body.ToArray()));
                Session = new ClientSession(
                    TestObjects.Schema(),
                    new SessionConfig(),
                    new SessionLimits(),
                    dispatcher,
                    TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ClientObjects(Session, TestObjects.Protocol(TestObjects.Channels()), Spawner);
                Objects.OnObjectMismatch += Mismatches.Add;
                Objects.OnOwnerChanged += OwnerChanges.Add;
                Session.Start(host.Listener.Connect(), host.Now);
            }

            public ulong PeerId(Host host) => Objects.LocalPeerId != 0 ? Objects.LocalPeerId : host.StartedPeers[index];
        }

        private sealed class RecordingSpawner : IObjectSpawner
        {
            public readonly List<SpawnedObject> Spawned = new List<SpawnedObject>();
            public readonly List<byte[][]> States = new List<byte[][]>();
            public readonly List<uint> Despawned = new List<uint>();

            public SpawnResult Spawn(in SpawnedObject spawned)
            {
                Spawned.Add(spawned);
                States.Add(spawned.States.Select(state => state.ToArray()).ToArray());
                return SpawnResult.Spawned;
            }

            public void Despawn(uint objectId) => Despawned.Add(objectId);
        }
    }
}
