using System;
using System.Collections.Generic;
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

namespace Fomoxa.Networking.Tests
{
    public sealed class NetworkObjectsTest
    {
        private const uint PrefabId = 0xA1;
        private const uint Fingerprint = 0xF1;
        private const ulong SceneObjectId = 0xABCD_0001_0000_0007;

        [Test]
        public void LatePeerGetsLocalPeerThenEveryLiveObjectInSpawnOrder()
        {
            var host = new Host();
            host.Connect();
            host.Run(20);
            ulong ownerId = host.StartedPeers[0];
            host.Transforms[1] = new Vector3(1, 2, 3);
            host.Transforms[3] = new Vector3(7, 8, 9);
            uint first = host.Objects.Spawn(PrefabId, 0);
            uint second = host.Objects.Spawn(PrefabId + 1, 0);
            uint third = host.Objects.Spawn(PrefabId + 2, ownerId);
            host.Objects.Despawn(second);

            Client client = host.Connect();
            host.Run(20);

            Assert.AreEqual(host.StartedPeers[1], client.Objects.LocalPeerId);
            Assert.AreEqual(2, client.Spawner.Spawned.Count);
            Assert.AreEqual(first, client.Spawner.Spawned[0].ObjectId);
            Assert.AreEqual(PrefabId, client.Spawner.Spawned[0].PrefabId);
            Assert.AreEqual(Fingerprint, client.Spawner.Spawned[0].PrefabFingerprint);
            Assert.AreEqual(new Vector3(1, 2, 3), client.Spawner.Spawned[0].Position);
            Assert.AreEqual(Quaternion.Identity, client.Spawner.Spawned[0].Rotation);
            Assert.AreEqual(Vector3.One, client.Spawner.Spawned[0].Scale);
            Assert.AreEqual(third, client.Spawner.Spawned[1].ObjectId);
            Assert.AreEqual(ownerId, client.Spawner.Spawned[1].OwnerId);
            Assert.AreEqual(new Vector3(7, 8, 9), client.Spawner.Spawned[1].Position);
            Assert.AreEqual(2, client.Objects.Count);
            Assert.IsFalse(client.Objects.TryGet(second, out _));
        }

        [Test]
        public void SpawnOwnerChangeAndDespawnReachEveryStartedPeer()
        {
            var host = new Host();
            Client owner = host.Connect();
            Client other = host.Connect();
            host.Run(20);
            var serverChanges = new List<ObjectOwnerChangedArgs>();
            host.Objects.OnOwnerChanged += serverChanges.Add;

            uint objectId = host.Objects.Spawn(PrefabId, 0);
            host.Run(10);
            Assert.IsTrue(host.Objects.ChangeOwner(objectId, owner.Objects.LocalPeerId));
            host.Run(10);

            Assert.AreEqual(1, serverChanges.Count);
            foreach (Client client in new[] { owner, other })
            {
                Assert.AreEqual(1, client.Spawner.Spawned.Count);
                Assert.IsTrue(client.Objects.TryGet(objectId, out ObjectRow row));
                Assert.AreEqual(owner.Objects.LocalPeerId, row.OwnerId);
                Assert.AreEqual(1, client.OwnerChanges.Count);
                Assert.AreEqual(0UL, client.OwnerChanges[0].PreviousOwnerId);
            }

            Assert.IsTrue(owner.Objects.IsOwner(objectId));
            Assert.IsFalse(other.Objects.IsOwner(objectId));

            Assert.IsTrue(host.Objects.Despawn(objectId));
            host.Run(10);

            Assert.IsFalse(host.Objects.Despawn(objectId));
            Assert.IsFalse(host.Objects.ChangeOwner(objectId, 0));
            foreach (Client client in new[] { owner, other })
            {
                CollectionAssert.AreEqual(new[] { objectId }, client.Spawner.Despawned);
                Assert.AreEqual(0, client.Objects.Count);
            }
        }

        [Test]
        public void ChangingToTheSameOwnerSendsNothing()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            uint objectId = host.Objects.Spawn(PrefabId, 0);

            Assert.IsTrue(host.Objects.ChangeOwner(objectId, 0));
            host.Run(10);

            Assert.AreEqual(0, client.OwnerChanges.Count);
        }

        [Test]
        public void ReplayGoesBeforeMessagesQueuedWhileThePeerWasStarting()
        {
            var host = new Host();
            host.Objects.Spawn(PrefabId, 0);
            host.Server.OnRemoteConnectionState += args =>
            {
                if (args.State == ConnectionState.Starting)
                {
                    host.Server.Send(args.PeerId, TestObjects.GreetingId, new byte[] { 1 });
                }
            };
            var seen = new List<(ulong LocalPeerId, int Objects)>();
            Client client = host.Connect(dispatcher =>
                dispatcher.Register(TestObjects.GreetingId, (peerId, payload) => seen.Add((host.Clients[0].Objects.LocalPeerId, host.Clients[0].Objects.Count))));

            host.Run(20);

            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(host.StartedPeers[0], seen[0].LocalPeerId);
            Assert.AreEqual(1, seen[0].Objects);
        }

        [Test]
        public void ObjectMessagesAreNotRefusedByTheQueueCapacity()
        {
            var host = new Host(new SessionLimits { MessageCapacity = 2 });
            for (int index = 0; index < 5; index++)
            {
                host.Objects.Spawn(PrefabId, 0);
            }

            Client client = host.Connect();
            host.Server.OnRemoteConnectionState += args =>
            {
                if (args.State == ConnectionState.Started)
                {
                    Assert.AreEqual(SendResult.Full, host.Server.Send(args.PeerId, TestObjects.GreetingId, new byte[] { 1 }));
                }
            };
            host.Run(20);

            Assert.AreEqual(5, client.Spawner.Spawned.Count);
            Assert.AreEqual(ConnectionState.Started, client.Session.State);
        }

        [Test]
        public void SpawnSourceExceptionReachesTheCallerAndAddsNoRow()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            host.Objects.Spawn(PrefabId, 0);
            host.Failing.Add(2);

            Assert.Throws<InvalidOperationException>(() => host.Objects.Spawn(PrefabId, 0));
            uint next = host.Objects.Spawn(PrefabId, 0);
            host.Run(10);

            Assert.AreEqual(3u, next);
            Assert.AreEqual(2, host.Objects.Count);
            Assert.IsFalse(host.Objects.TryGet(2, out _));
            Assert.AreEqual(2, client.Spawner.Spawned.Count);
            Assert.AreEqual(3u, client.Spawner.Spawned[1].ObjectId);
        }

        [Test]
        public void SpawnSourceExceptionDuringReplayDisconnectsOnlyThatPeer()
        {
            var host = new Host();
            Client first = host.Connect();
            host.Run(20);
            uint objectId = host.Objects.Spawn(PrefabId, 0);
            host.Run(10);
            host.Failing.Add(objectId);

            Client second = host.Connect();
            host.Run(20);

            Assert.AreEqual(1, host.Exceptions.Count);
            Assert.AreEqual(TestObjects.SpawnId, host.Exceptions[0].MessageId);
            Assert.IsInstanceOf<InvalidOperationException>(host.Exceptions[0].Exception);
            Assert.AreEqual(1, host.StartedPeers.Count);
            Assert.AreEqual(1, host.Stopped.Count);
            Assert.AreEqual(host.Exceptions[0].PeerId, host.Stopped[0].PeerId);
            Assert.AreEqual(StopReason.HandlerException, host.Stopped[0].Reason);
            Assert.AreEqual(ConnectionState.Started, first.Session.State);
            Assert.AreEqual(ConnectionState.Stopped, second.Session.State);
            Assert.AreEqual(0, second.Spawner.Spawned.Count);
            Assert.AreEqual(ServerState.Started, host.Server.State);
        }

        [Test]
        public void ObjectIdsWrapAroundAndSkipIdsStillInUse()
        {
            var host = new Host();
            host.Objects.Spawn(PrefabId, 0);
            host.Objects.Spawn(PrefabId, 0);
            host.Objects.NextObjectId = uint.MaxValue;

            Assert.AreEqual(uint.MaxValue, host.Objects.Spawn(PrefabId, 0));
            Assert.AreEqual(3u, host.Objects.Spawn(PrefabId, 0));
        }

        [Test]
        public void StoppingTheServerClearsTheTableAndRestartsIdsFromOne()
        {
            var host = new Host();
            host.Objects.Spawn(PrefabId, 0);
            host.Objects.Spawn(PrefabId, 0);

            host.Server.Stop();
            Assert.Throws<InvalidOperationException>(() => host.Objects.Spawn(PrefabId, 0));
            host.Server.Start(new LoopbackListener(64));

            Assert.AreEqual(0, host.Objects.Count);
            Assert.AreEqual(1u, host.Objects.Spawn(PrefabId, 0));
        }

        [Test]
        public void EndedClientSessionDespawnsEveryObjectInReverseSpawnOrder()
        {
            var host = new Host();
            host.Objects.Spawn(PrefabId, 0);
            host.Objects.Spawn(PrefabId, 0);
            Client client = host.Connect();
            host.Run(20);

            client.Session.Stop();

            CollectionAssert.AreEqual(new[] { 2u, 1u }, client.Spawner.Despawned);
            Assert.AreEqual(0, client.Objects.Count);
            Assert.AreEqual(0UL, client.Objects.LocalPeerId);
        }

        [Test]
        public void UnknownPrefabStopsTheClientWithPrefabMismatch()
        {
            Client client = ClientAfter((host, peerId) => host.Objects.Spawn(PrefabId, 0), SpawnResult.UnknownPrefab);

            AssertMismatch(client, ObjectMismatchKind.UnknownPrefab, 1, PrefabId, StopReason.PrefabMismatch);
            Assert.AreEqual(0, client.Objects.Count);
            Assert.AreEqual(0, client.Spawner.Despawned.Count);
        }

        [Test]
        public void IncompatiblePrefabStopsTheClientWithPrefabMismatch()
        {
            Client client = ClientAfter((host, peerId) => host.Objects.Spawn(PrefabId, 0), SpawnResult.IncompatiblePrefab);

            AssertMismatch(client, ObjectMismatchKind.IncompatiblePrefab, 1, PrefabId, StopReason.PrefabMismatch);
        }

        [Test]
        public void DuplicateSpawnStopsTheClientWithObjectMismatch()
        {
            Client client = ClientAfter((host, peerId) =>
            {
                host.Server.Send(peerId, TestObjects.SpawnId, TestObjects.Spawn(7, PrefabId, 0));
                host.Server.Send(peerId, TestObjects.SpawnId, TestObjects.Spawn(7, PrefabId, 0));
            });

            AssertMismatch(client, ObjectMismatchKind.DuplicateObject, 7, PrefabId, StopReason.ObjectMismatch);
            CollectionAssert.AreEqual(new[] { 7u }, client.Spawner.Despawned);
        }

        [Test]
        public void DespawnOfAnUnknownObjectStopsTheClientWithObjectMismatch()
        {
            Client client = ClientAfter((host, peerId) => host.Server.Send(peerId, TestObjects.DespawnId, TestObjects.Despawn(9)));

            AssertMismatch(client, ObjectMismatchKind.UnknownObject, 9, 0, StopReason.ObjectMismatch);
        }

        [Test]
        public void OwnerChangeOfAnUnknownObjectStopsTheClientWithObjectMismatch()
        {
            Client client = ClientAfter((host, peerId) => host.Server.Send(peerId, TestObjects.OwnerChangeId, TestObjects.OwnerChange(9, 1)));

            AssertMismatch(client, ObjectMismatchKind.UnknownObject, 9, 0, StopReason.ObjectMismatch);
        }

        [TestCase(7u, (byte)8, 0)]
        [TestCase(7u, (byte)1, 2)]
        [TestCase(0u, (byte)0, 0)]
        public void InvalidSpawnStopsTheClientWithObjectMismatch(uint objectId, byte mask, int valueCount)
        {
            Client client = ClientAfter((host, peerId) =>
                host.Server.Send(peerId, TestObjects.SpawnId, TestObjects.Spawn(objectId, PrefabId, 0, mask, new float[valueCount])));

            AssertMismatch(client, ObjectMismatchKind.InvalidSpawn, objectId, PrefabId, StopReason.ObjectMismatch);
            Assert.AreEqual(0, client.Spawner.Spawned.Count);
        }

        [Test]
        public void SpawnWithoutAPrefabIdStopsTheClientWithObjectMismatch()
        {
            Client client = ClientAfter((host, peerId) => host.Server.Send(peerId, TestObjects.SpawnId, TestObjects.Spawn(7, 0, 0)));

            AssertMismatch(client, ObjectMismatchKind.InvalidSpawn, 7, 0, StopReason.ObjectMismatch);
            Assert.AreEqual(0, client.Spawner.Spawned.Count);
        }

        [Test]
        public void ServerRefusesToSpawnWithoutAPrefabId()
        {
            var host = new Host();

            Assert.Throws<ArgumentException>(() => host.Objects.Spawn(0, 0));
            Assert.AreEqual(0, host.Objects.Count);
            Assert.AreEqual(1u, host.Objects.Spawn(PrefabId, 0));
        }

        [Test]
        public void UndecodableObjectMessageStopsTheClientWithObjectMismatch()
        {
            Client client = ClientAfter((host, peerId) => host.Server.Send(peerId, TestObjects.DespawnId, new byte[] { 1, 2 }));

            AssertMismatch(client, ObjectMismatchKind.InvalidMessage, 0, 0, StopReason.ObjectMismatch);
        }

        [Test]
        public void SpawnerExceptionIsAHandlerExceptionAndLeavesNoRow()
        {
            var host = new Host();
            Client client = host.Connect();
            client.Spawner.Throws = true;
            host.Run(20);

            host.Objects.Spawn(PrefabId, 0);
            host.Run(10);

            Assert.AreEqual(1, client.Exceptions.Count);
            Assert.AreEqual(TestObjects.SpawnId, client.Exceptions[0].MessageId);
            Assert.AreEqual(0, client.Mismatches.Count);
            Assert.AreEqual(StopReason.HandlerException, client.States[client.States.Count - 1].Reason);
            Assert.AreEqual(0, client.Spawner.Despawned.Count);
        }

        [Test]
        public void MismatchIsNotRetriedByTheReconnector()
        {
            var host = new Host();
            Client client = host.Connect(reconnect: true);
            host.Run(20);

            host.Server.Send(host.StartedPeers[0], TestObjects.DespawnId, TestObjects.Despawn(9));
            host.Run(20);

            Assert.AreEqual(1, client.Connectors);
            Assert.AreEqual(StopReason.ObjectMismatch, client.RetryStates[client.RetryStates.Count - 1].Reason);
            Assert.IsFalse(client.RetryStates[client.RetryStates.Count - 1].WillRetry);
        }

        [Test]
        public void SceneSpawnReachesStartedAndLatePeersInSpawnOrder()
        {
            var host = new Host();
            Client early = host.Connect();
            host.Run(20);
            host.Transforms[2] = new Vector3(4, 5, 6);
            uint prefabObject = host.Objects.Spawn(PrefabId, 0);
            uint sceneObject = host.Objects.SpawnScene(SceneObjectId, early.Objects.LocalPeerId);
            host.Run(10);

            Client late = host.Connect();
            host.Run(20);

            Assert.IsTrue(host.Objects.TryGet(sceneObject, out ObjectRow serverRow));
            Assert.AreEqual(SceneObjectId, serverRow.SceneObjectId);
            Assert.AreEqual(0u, serverRow.PrefabId);
            foreach (Client client in new[] { early, late })
            {
                Assert.AreEqual(2, client.Spawner.Spawned.Count);
                Assert.AreEqual(prefabObject, client.Spawner.Spawned[0].ObjectId);
                Assert.AreEqual(0UL, client.Spawner.Spawned[0].SceneObjectId);
                SpawnedObject spawned = client.Spawner.Spawned[1];
                Assert.AreEqual(sceneObject, spawned.ObjectId);
                Assert.AreEqual(0u, spawned.PrefabId);
                Assert.AreEqual(SceneObjectId, spawned.SceneObjectId);
                Assert.AreEqual(Fingerprint, spawned.PrefabFingerprint);
                Assert.AreEqual(early.Objects.LocalPeerId, spawned.OwnerId);
                Assert.AreEqual(new Vector3(4, 5, 6), spawned.Position);
                Assert.IsTrue(client.Objects.TryGet(sceneObject, out ObjectRow row));
                Assert.AreEqual(SceneObjectId, row.SceneObjectId);
            }
        }

        [Test]
        public void SceneObjectIdsAreSpawnedOnceAtATime()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);

            Assert.Throws<ArgumentException>(() => host.Objects.SpawnScene(0, 0));
            uint first = host.Objects.SpawnScene(SceneObjectId, 0);
            Assert.Throws<ArgumentException>(() => host.Objects.SpawnScene(SceneObjectId, 0));
            Assert.IsTrue(host.Objects.Despawn(first));
            uint second = host.Objects.SpawnScene(SceneObjectId, 0);
            host.Run(10);

            Assert.AreNotEqual(first, second);
            Assert.AreEqual(2, client.Spawner.Spawned.Count);
            CollectionAssert.AreEqual(new[] { first }, client.Spawner.Despawned);
            Assert.AreEqual(ConnectionState.Started, client.Session.State);
            host.Server.Stop();
            host.Server.Start(new LoopbackListener(64));
            Assert.AreEqual(1u, host.Objects.SpawnScene(SceneObjectId, 0));
        }

        [Test]
        public void UnknownSceneObjectStopsTheClientWithPrefabMismatch()
        {
            Client client = ClientAfter((host, peerId) => host.Objects.SpawnScene(SceneObjectId, 0), SpawnResult.UnknownSceneObject);

            AssertMismatch(client, ObjectMismatchKind.UnknownSceneObject, 1, 0, StopReason.PrefabMismatch, SceneObjectId);
            Assert.AreEqual(0, client.Objects.Count);
            Assert.AreEqual(0, client.Spawner.Despawned.Count);
        }

        [Test]
        public void IncompatibleSceneObjectStopsTheClientWithPrefabMismatch()
        {
            Client client = ClientAfter((host, peerId) => host.Objects.SpawnScene(SceneObjectId, 0), SpawnResult.IncompatibleSceneObject);

            AssertMismatch(client, ObjectMismatchKind.IncompatibleSceneObject, 1, 0, StopReason.PrefabMismatch, SceneObjectId);
        }

        [Test]
        public void SceneObjectIdOnTwoLiveObjectsStopsTheClientWithObjectMismatch()
        {
            Client client = ClientAfter((host, peerId) =>
            {
                host.Server.Send(peerId, TestObjects.SceneSpawnId, TestObjects.SceneSpawn(7, SceneObjectId, Fingerprint, 0));
                host.Server.Send(peerId, TestObjects.SceneSpawnId, TestObjects.SceneSpawn(8, SceneObjectId, Fingerprint, 0));
            });

            AssertMismatch(client, ObjectMismatchKind.DuplicateObject, 8, 0, StopReason.ObjectMismatch, SceneObjectId);
            CollectionAssert.AreEqual(new[] { 7u }, client.Spawner.Despawned);
        }

        [Test]
        public void SceneObjectIdIsFreeAgainAfterItsDespawn()
        {
            Client client = ClientAfter((host, peerId) =>
            {
                host.Server.Send(peerId, TestObjects.SceneSpawnId, TestObjects.SceneSpawn(7, SceneObjectId, Fingerprint, 0));
                host.Server.Send(peerId, TestObjects.DespawnId, TestObjects.Despawn(7));
                host.Server.Send(peerId, TestObjects.SceneSpawnId, TestObjects.SceneSpawn(8, SceneObjectId, Fingerprint, 0));
            });

            Assert.AreEqual(0, client.Mismatches.Count);
            Assert.AreEqual(2, client.Spawner.Spawned.Count);
            Assert.IsTrue(client.Objects.TryGet(8, out _));
        }

        [TestCase(0u, 7UL, (byte)0, 0)]
        [TestCase(7u, 0UL, (byte)0, 0)]
        [TestCase(7u, 7UL, (byte)1, 2)]
        public void InvalidSceneSpawnStopsTheClientWithObjectMismatch(uint objectId, ulong sceneObjectId, byte mask, int valueCount)
        {
            Client client = ClientAfter((host, peerId) =>
                host.Server.Send(peerId, TestObjects.SceneSpawnId, TestObjects.SceneSpawn(objectId, sceneObjectId, Fingerprint, 0, mask, new float[valueCount])));

            AssertMismatch(client, ObjectMismatchKind.InvalidSpawn, objectId, 0, StopReason.ObjectMismatch, sceneObjectId);
            Assert.AreEqual(0, client.Spawner.Spawned.Count);
        }

        [Test]
        public void SpawnSourceExceptionDuringSceneReplayNamesTheSceneSpawnMessage()
        {
            var host = new Host();
            uint objectId = host.Objects.SpawnScene(SceneObjectId, 0);
            host.Failing.Add(objectId);

            host.Connect();
            host.Run(20);

            Assert.AreEqual(1, host.Exceptions.Count);
            Assert.AreEqual(TestObjects.SceneSpawnId, host.Exceptions[0].MessageId);
        }

        [Test]
        public void OwnerMustBeZeroOrAStartedPeer()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            ulong peerId = client.Objects.LocalPeerId;
            uint objectId = host.Objects.Spawn(PrefabId, 0);

            Assert.Throws<ArgumentException>(() => host.Objects.Spawn(PrefabId, peerId + 1));
            Assert.Throws<ArgumentException>(() => host.Objects.SpawnScene(SceneObjectId, peerId + 1));
            Assert.IsFalse(host.Objects.ChangeOwner(objectId, peerId + 1));
            Assert.IsTrue(host.Objects.TryGet(objectId, out ObjectRow row));
            Assert.AreEqual(0UL, row.OwnerId);
            Assert.AreEqual(1, host.Objects.Count);
            Assert.IsTrue(host.Objects.ChangeOwner(objectId, peerId));
            Assert.AreEqual(2u, host.Objects.Spawn(PrefabId, peerId));
        }

        [Test]
        public void PeerStoppedDespawnsItsObjectsInReverseSpawnOrderBeforeTheApplicationSeesIt()
        {
            var host = new Host();
            Client leaving = host.Connect();
            Client staying = host.Connect();
            host.Run(20);
            ulong leavingId = leaving.Objects.LocalPeerId;
            uint first = host.Objects.Spawn(PrefabId, leavingId);
            uint kept = host.Objects.Spawn(PrefabId, staying.Objects.LocalPeerId);
            uint second = host.Objects.SpawnScene(SceneObjectId, leavingId);
            uint serverOwned = host.Objects.Spawn(PrefabId, 0);
            host.Run(10);
            int countSeenByApplication = -1;
            host.Server.OnRemoteConnectionState += args =>
            {
                if (args.PeerId == leavingId && args.State == ConnectionState.Stopped)
                {
                    countSeenByApplication = host.Objects.Count;
                }
            };

            leaving.Session.Stop();
            host.Run(10);

            Assert.AreEqual(2, countSeenByApplication);
            Assert.IsFalse(host.Objects.TryGet(first, out _));
            Assert.IsFalse(host.Objects.TryGet(second, out _));
            Assert.IsTrue(host.Objects.TryGet(kept, out _));
            Assert.IsTrue(host.Objects.TryGet(serverOwned, out _));
            CollectionAssert.AreEqual(new[] { second, first }, staying.Spawner.Despawned);
            Assert.AreEqual(ConnectionState.Started, staying.Session.State);
            Assert.DoesNotThrow(() => host.Objects.SpawnScene(SceneObjectId, 0));
        }

        [Test]
        public void ObjectKeptByTheApplicationGoesBackToTheServer()
        {
            var host = new Host();
            Client leaving = host.Connect();
            Client staying = host.Connect();
            host.Run(20);
            ulong leavingId = leaving.Objects.LocalPeerId;
            uint despawned = host.Objects.Spawn(PrefabId, leavingId);
            uint kept = host.Objects.Spawn(PrefabId, leavingId);
            host.Objects.DespawnWithOwner = objectId => objectId != kept;
            var serverChanges = new List<ObjectOwnerChangedArgs>();
            host.Objects.OnOwnerChanged += serverChanges.Add;
            host.Run(10);

            leaving.Session.Stop();
            host.Run(10);

            Assert.IsFalse(host.Objects.TryGet(despawned, out _));
            Assert.IsTrue(host.Objects.TryGet(kept, out ObjectRow row));
            Assert.AreEqual(0UL, row.OwnerId);
            Assert.AreEqual(1, serverChanges.Count);
            Assert.AreEqual(leavingId, serverChanges[0].PreviousOwnerId);
            Assert.AreEqual(1, staying.OwnerChanges.Count);
            Assert.AreEqual(kept, staying.OwnerChanges[0].ObjectId);
            Assert.AreEqual(0UL, staying.OwnerChanges[0].OwnerId);
        }

        [Test]
        public void DespawnerRunsForEachDespawnedObjectOfTheLeavingPeer()
        {
            var host = new Host();
            Client leaving = host.Connect();
            host.Run(20);
            ulong leavingId = leaving.Objects.LocalPeerId;
            uint first = host.Objects.Spawn(PrefabId, leavingId);
            uint second = host.Objects.Spawn(PrefabId, leavingId);
            var despawnedBy = new List<uint>();
            host.Objects.Despawner = objectId =>
            {
                despawnedBy.Add(objectId);
                host.Objects.Despawn(objectId);
            };

            leaving.Session.Stop();
            host.Run(10);

            CollectionAssert.AreEqual(new[] { second, first }, despawnedBy);
            Assert.AreEqual(0, host.Objects.Count);
        }

        [Test]
        public void StoppingTheServerSkipsTheOwnerHandling()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            host.Objects.Spawn(PrefabId, client.Objects.LocalPeerId);
            int asked = 0;
            host.Objects.DespawnWithOwner = objectId =>
            {
                asked++;
                return true;
            };

            host.Server.Stop();

            Assert.AreEqual(0, asked);
            Assert.AreEqual(0, host.Objects.Count);
        }

        [Test]
        public void SpawnAndSceneSpawnCarryTheStatesOfTheSpawnSource()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            host.States[1] = new[] { new byte[] { 1, 2 }, new byte[0], new byte[] { 3 } };
            host.States[2] = new[] { new byte[] { 9 } };

            host.Objects.Spawn(PrefabId, 0);
            host.Objects.SpawnScene(SceneObjectId, 0);
            host.Run(20);

            Assert.AreEqual(2, client.Spawner.States.Count);
            CollectionAssert.AreEqual(host.States[1], client.Spawner.States[0]);
            CollectionAssert.AreEqual(host.States[2], client.Spawner.States[1]);
        }

        [Test]
        public void SpawnToALatePeerCarriesTheStatesTheSourceReturnsThen()
        {
            var host = new Host();
            host.States[1] = new[] { new byte[] { 1 } };
            uint objectId = host.Objects.Spawn(PrefabId, 0);
            host.States[1] = new[] { new byte[] { 2 } };

            Client client = host.Connect();
            host.Run(20);

            Assert.AreEqual(1u, objectId);
            CollectionAssert.AreEqual(new[] { new byte[] { 2 } }, client.Spawner.States[0]);
        }

        [Test]
        public void SpawnWithoutStatesCarriesAnEmptyArray()
        {
            Client client = ClientAfter((host, peerId) => host.Objects.Spawn(PrefabId, 0));

            Assert.AreEqual(1, client.Spawner.States.Count);
            Assert.IsEmpty(client.Spawner.States[0]);
        }

        [Test]
        public void SpawnerReportingAnInvalidSpawnStopsTheClientWithObjectMismatch()
        {
            Client client = ClientAfter((host, peerId) => host.Objects.Spawn(PrefabId, 0), SpawnResult.InvalidSpawn);

            AssertMismatch(client, ObjectMismatchKind.InvalidSpawn, 1, PrefabId, StopReason.ObjectMismatch);
            Assert.AreEqual(0, client.Objects.Count);
        }

        [Test]
        public void ObjectModelsOffTheReliableChannelAreRejected()
        {
            Assert.Throws<ArgumentException>(() => TestObjects.Protocol(new MessageChannels()));
        }

        private static Client ClientAfter(Action<Host, ulong> send, SpawnResult result = SpawnResult.Spawned)
        {
            var host = new Host();
            Client client = host.Connect();
            client.Spawner.Result = result;
            host.Run(20);
            send(host, host.StartedPeers[0]);
            host.Run(20);
            return client;
        }

        private static void AssertMismatch(Client client, ObjectMismatchKind kind, uint objectId, uint prefabId, StopReason reason, ulong sceneObjectId = 0)
        {
            Assert.AreEqual(1, client.Mismatches.Count);
            Assert.AreEqual(kind, client.Mismatches[0].Kind);
            Assert.AreEqual(objectId, client.Mismatches[0].ObjectId);
            Assert.AreEqual(prefabId, client.Mismatches[0].PrefabId);
            Assert.AreEqual(sceneObjectId, client.Mismatches[0].SceneObjectId);
            Assert.AreEqual(ConnectionState.Stopped, client.Session.State);
            Assert.AreEqual(reason, client.States[client.States.Count - 1].Reason);
            CollectionAssert.AreEqual(new[] { "mismatch", "stopped" }, client.Log.GetRange(client.Log.Count - 2, 2));
        }

        private sealed class Host
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly ServerSession Server;
            public readonly ServerObjects Objects;
            public readonly Dictionary<uint, Vector3> Transforms = new Dictionary<uint, Vector3>();
            public readonly Dictionary<uint, byte[][]> States = new Dictionary<uint, byte[][]>();
            public readonly HashSet<uint> Failing = new HashSet<uint>();
            public readonly List<ulong> StartedPeers = new List<ulong>();
            public readonly List<ConnectionStateArgs> Stopped = new List<ConnectionStateArgs>();
            public readonly List<HandlerExceptionArgs> Exceptions = new List<HandlerExceptionArgs>();
            public readonly List<Client> Clients = new List<Client>();
            public TimeSpan Now = TimeSpan.Zero;

            public Host(SessionLimits limits = null)
            {
                Server = new ServerSession(
                    TestObjects.Schema(),
                    new SessionConfig(),
                    limits ?? new SessionLimits(),
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

            public Client Connect(Action<MessageDispatcher> register = null, bool reconnect = false)
            {
                var client = new Client(this, register, reconnect);
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
                        client.Tick(Now);
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
            public readonly ClientReconnector Reconnector;
            public readonly RecordingSpawner Spawner = new RecordingSpawner();
            public readonly List<ConnectionStateArgs> States = new List<ConnectionStateArgs>();
            public readonly List<ConnectionStateArgs> RetryStates = new List<ConnectionStateArgs>();
            public readonly List<ObjectMismatchArgs> Mismatches = new List<ObjectMismatchArgs>();
            public readonly List<ObjectOwnerChangedArgs> OwnerChanges = new List<ObjectOwnerChangedArgs>();
            public readonly List<HandlerExceptionArgs> Exceptions = new List<HandlerExceptionArgs>();
            public readonly List<string> Log = new List<string>();
            public int Connectors;

            public Client(Host host, Action<MessageDispatcher> register, bool reconnect)
            {
                var dispatcher = new MessageDispatcher(TestObjects.Schema());
                register?.Invoke(dispatcher);
                Session = new ClientSession(
                    TestObjects.Schema(),
                    new SessionConfig(),
                    new SessionLimits(),
                    dispatcher,
                    TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ClientObjects(Session, TestObjects.Protocol(TestObjects.Channels()), Spawner);
                Objects.OnObjectMismatch += args =>
                {
                    Mismatches.Add(args);
                    Log.Add("mismatch");
                };
                Objects.OnOwnerChanged += OwnerChanges.Add;
                Session.OnHandlerException += Exceptions.Add;
                Session.OnClientConnectionState += args =>
                {
                    States.Add(args);
                    if (args.State == ConnectionState.Stopped)
                    {
                        Log.Add("stopped");
                    }
                };
                if (!reconnect)
                {
                    Session.Start(host.Listener.Connect(), host.Now);
                    return;
                }

                Reconnector = new ClientReconnector(Session, new ReconnectPolicy { Interval = TimeSpan.FromMilliseconds(16) });
                Reconnector.OnClientConnectionState += RetryStates.Add;
                Reconnector.Start(() =>
                {
                    Connectors++;
                    return new LoopbackConnector(host.Listener);
                }, host.Now);
            }

            public void Tick(TimeSpan now)
            {
                if (Reconnector != null)
                {
                    Reconnector.Tick(now);
                    return;
                }

                Session.Tick(now);
            }
        }

        private sealed class RecordingSpawner : IObjectSpawner
        {
            public readonly List<SpawnedObject> Spawned = new List<SpawnedObject>();
            public readonly List<byte[][]> States = new List<byte[][]>();
            public readonly List<uint> Despawned = new List<uint>();
            public SpawnResult Result = SpawnResult.Spawned;
            public bool Throws;

            public SpawnResult Spawn(in SpawnedObject spawned)
            {
                if (Throws)
                {
                    throw new InvalidOperationException("spawner failed");
                }

                Spawned.Add(spawned);
                States.Add(spawned.States.Select(state => state.ToArray()).ToArray());
                return Result;
            }

            public void Despawn(uint objectId) => Despawned.Add(objectId);
        }

        private sealed class LoopbackConnector : ITransportConnector
        {
            private readonly LoopbackListener listener;

            public LoopbackConnector(LoopbackListener listener)
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
