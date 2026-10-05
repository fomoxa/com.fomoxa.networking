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
    public sealed class ScenesTest
    {
        private const uint PrefabId = 0xA1;
        private const uint Arena = 0x5C_0001;
        private const uint Lobby = 0x5C_0002;
        private const uint Room = 0x5C_0003;

        [Test]
        public void APeerGetsTheGlobalScenesBeforeAnySpawnAndObjectsOfAPendingSceneWait()
        {
            var host = new Host();
            host.Scenes.LoadGlobal(new[] { Arena });
            uint outside = host.Objects.Spawn(PrefabId, 0);
            uint inside = host.Objects.Spawn(PrefabId, 0, Arena);
            Client client = host.Connect(manual: true);
            host.Run(20);

            CollectionAssert.AreEqual(new[] { $"load {Arena:X}", $"spawn {outside}" }, client.Log);
            Assert.IsFalse(host.Objects.IsObserver(inside, client.PeerId(host)));

            client.Scenes.CompleteNext();
            host.Run(10);

            CollectionAssert.AreEqual(new[] { $"load {Arena:X}", $"spawn {outside}", $"spawn {inside}" }, client.Log);
            Assert.AreEqual(Arena, client.Spawner.Spawned[1].SceneId);
            Assert.IsTrue(host.Scenes.HasLoaded(client.PeerId(host), Arena));
        }

        [Test]
        public void AnOwnerSeesItsObjectOnlyAfterLoadingTheScene()
        {
            var host = new Host();
            Client owner = host.Connect(manual: true);
            host.Run(20);
            host.Scenes.LoadGlobal(new[] { Arena });
            host.Run(10);
            uint objectId = host.Objects.Spawn(PrefabId, owner.PeerId(host), Arena);
            host.Run(10);

            Assert.IsFalse(host.Objects.IsObserver(objectId, owner.PeerId(host)));
            owner.Scenes.CompleteNext();
            host.Run(10);

            Assert.IsTrue(host.Objects.IsObserver(objectId, owner.PeerId(host)));
            Assert.IsTrue(owner.Objects.IsOwner(objectId));
        }

        [Test]
        public void ReplacingTheGlobalScenesDespawnsTheirObjectsBeforeTheUnload()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            var removed = new List<uint>();
            var added = new List<uint>();
            host.Scenes.OnSceneRemoved += removed.Add;
            host.Scenes.OnSceneAdded += added.Add;
            host.Scenes.LoadGlobal(new[] { Arena });
            host.Run(10);
            uint first = host.Objects.Spawn(PrefabId, 0, Arena);
            uint second = host.Objects.Spawn(PrefabId, 0, Arena);
            host.Run(10);
            client.Log.Clear();

            host.Scenes.LoadGlobal(new[] { Lobby }, replace: true);
            host.Run(10);

            CollectionAssert.AreEqual(new[] { $"despawn {second}", $"despawn {first}", $"unload {Arena:X}", $"load {Lobby:X}" }, client.Log);
            CollectionAssert.AreEqual(new[] { Lobby }, host.Scenes.Global);
            CollectionAssert.AreEqual(new[] { Arena, Lobby }, added);
            CollectionAssert.AreEqual(new[] { Arena }, removed);
            Assert.AreEqual(0, host.Objects.Count);
        }

        [Test]
        public void ADespawnerRunsForEveryObjectOfAnUnloadedSceneInReverseSpawnOrder()
        {
            var host = new Host();
            host.Connect();
            host.Run(20);
            var despawned = new List<uint>();
            host.Objects.Despawner = objectId =>
            {
                despawned.Add(objectId);
                host.Objects.Despawn(objectId);
            };
            host.Scenes.LoadGlobal(new[] { Arena });
            uint first = host.Objects.Spawn(PrefabId, 0, Arena);
            uint outside = host.Objects.Spawn(PrefabId, 0);
            uint second = host.Objects.Spawn(PrefabId, 0, Arena);

            host.Scenes.UnloadGlobal(new[] { Arena });

            CollectionAssert.AreEqual(new[] { second, first }, despawned);
            Assert.IsTrue(host.Objects.TryGet(outside, out _));
        }

        [Test]
        public void APerPeerSceneReachesOnlyItsPeersAndHidesItsObjectsFromOthers()
        {
            var host = new Host();
            Client member = host.Connect();
            Client other = host.Connect();
            host.Run(20);

            Assert.AreEqual(1, host.Scenes.LoadForPeers(Room, new[] { member.PeerId(host), 999UL }));
            host.Run(10);
            uint objectId = host.Objects.Spawn(PrefabId, 0, Room);
            host.Run(10);

            CollectionAssert.AreEqual(new[] { $"load {Room:X}", $"spawn {objectId}" }, member.Log);
            Assert.AreEqual(0, other.Log.Count);
            CollectionAssert.AreEqual(new[] { member.PeerId(host) }, host.Scenes.PeersOf(Room));
            Assert.AreEqual(0, host.Scenes.LoadForPeers(Room, new[] { member.PeerId(host) }));
        }

        [Test]
        public void LoadingAPerPeerSceneGloballyPromotesItAndOnlyNewPeersLoadIt()
        {
            var host = new Host();
            Client member = host.Connect();
            Client other = host.Connect();
            host.Run(20);
            var added = new List<uint>();
            host.Scenes.OnSceneAdded += added.Add;
            host.Scenes.LoadForPeers(Room, new[] { member.PeerId(host) });
            host.Run(10);

            host.Scenes.LoadGlobal(new[] { Room });
            host.Run(10);

            Assert.IsTrue(host.Scenes.IsGlobal(Room));
            CollectionAssert.AreEqual(new[] { Room }, added);
            CollectionAssert.AreEqual(new[] { $"load {Room:X}" }, member.Log);
            CollectionAssert.AreEqual(new[] { $"load {Room:X}" }, other.Log);
            Assert.Throws<ArgumentException>(() => host.Scenes.LoadForPeers(Room, new[] { member.PeerId(host) }));
            Assert.Throws<ArgumentException>(() => host.Scenes.UnloadForPeers(Room, new[] { member.PeerId(host) }));
            Assert.Throws<ArgumentException>(() => host.Scenes.Unload(Room));
        }

        [Test]
        public void UnloadingThePeersOfARoomDespawnsForThemAndTheLastOneUnloadsTheRoom()
        {
            var host = new Host();
            Client leaving = host.Connect();
            Client staying = host.Connect();
            host.Run(20);
            var removed = new List<uint>();
            host.Scenes.OnSceneRemoved += removed.Add;
            host.Scenes.LoadForPeers(Room, new[] { leaving.PeerId(host), staying.PeerId(host) });
            host.Run(10);
            uint objectId = host.Objects.Spawn(PrefabId, 0, Room);
            host.Run(10);
            leaving.Log.Clear();

            Assert.AreEqual(1, host.Scenes.UnloadForPeers(Room, new[] { leaving.PeerId(host), 999UL }));
            host.Run(10);

            CollectionAssert.AreEqual(new[] { $"despawn {objectId}", $"unload {Room:X}" }, leaving.Log);
            Assert.IsTrue(host.Objects.IsObserver(objectId, staying.PeerId(host)));
            Assert.AreEqual(0, removed.Count);

            host.Scenes.UnloadForPeers(Room, new[] { staying.PeerId(host) });
            host.Run(10);

            CollectionAssert.AreEqual(new[] { Room }, removed);
            Assert.IsFalse(host.Scenes.Contains(Room));
            Assert.AreEqual(0, host.Objects.Count);
        }

        [Test]
        public void ARoomOutlivesItsLastPeerDisconnectingUntilItIsUnloaded()
        {
            var host = new Host();
            Client member = host.Connect();
            host.Run(20);
            var removed = new List<uint>();
            host.Scenes.OnSceneRemoved += removed.Add;
            host.Scenes.LoadForPeers(Room, new[] { member.PeerId(host) });
            uint objectId = host.Objects.Spawn(PrefabId, 0, Room);
            host.Run(10);

            host.Server.Disconnect(member.PeerId(host));

            Assert.IsTrue(host.Scenes.Contains(Room));
            Assert.AreEqual(0, host.Scenes.PeersOf(Room).Count);
            Assert.IsTrue(host.Objects.TryGet(objectId, out _));
            Assert.AreEqual(0, removed.Count);

            host.Scenes.Unload(Room);

            CollectionAssert.AreEqual(new[] { Room }, removed);
            Assert.IsFalse(host.Objects.TryGet(objectId, out _));
        }

        [Test]
        public void TheClientRunsSceneStepsInOrderAndAnUnloadDropsAPendingLoad()
        {
            var host = new Host();
            Client client = host.Connect(manual: true);
            host.Run(20);

            host.Scenes.LoadGlobal(new[] { Arena });
            host.Scenes.LoadGlobal(new[] { Lobby });
            host.Run(10);
            host.Scenes.UnloadGlobal(new[] { Lobby });
            host.Run(10);
            client.Scenes.CompleteNext();
            host.Run(10);

            CollectionAssert.AreEqual(new[] { $"load {Arena:X}" }, client.Log);
            CollectionAssert.AreEquivalent(new[] { Arena }, client.Objects.LoadedScenes);
        }

        [Test]
        public void AReconnectKeepsLoadedScenesAndTheFullLoadUnloadsTheRest()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            host.Scenes.LoadGlobal(new[] { Arena, Lobby });
            host.Run(10);
            client.Session.Stop();
            host.Run(5);
            host.Scenes.UnloadGlobal(new[] { Arena });
            client.Log.Clear();

            client.Session.Start(host.Listener.Connect(), host.Now);
            host.Run(20);
            uint objectId = host.Objects.Spawn(PrefabId, 0, Lobby);
            host.Run(10);

            CollectionAssert.AreEqual(new[] { $"unload {Arena:X}", $"spawn {objectId}" }, client.Log);
            CollectionAssert.AreEquivalent(new[] { Lobby }, client.Objects.LoadedScenes);
        }

        [Test]
        public void AStopDuringALoadLetsTheLoadFinishAndTheFullLoadUnloadsItAfterwards()
        {
            var host = new Host();
            Client client = host.Connect(manual: true);
            host.Run(20);
            host.Scenes.LoadGlobal(new[] { Arena });
            host.Run(10);
            client.Session.Stop();
            host.Run(5);
            host.Scenes.UnloadGlobal(new[] { Arena });
            host.Scenes.LoadGlobal(new[] { Lobby });

            client.Session.Start(host.Listener.Connect(), host.Now);
            host.Run(20);
            CollectionAssert.AreEqual(new[] { $"load {Arena:X}" }, client.Log);

            client.Scenes.CompleteNext();
            host.Run(10);
            client.Scenes.CompleteNext();
            host.Run(10);

            CollectionAssert.AreEqual(new[] { $"load {Arena:X}", $"unload {Arena:X}", $"load {Lobby:X}" }, client.Log);
            CollectionAssert.AreEquivalent(new[] { Lobby }, client.Objects.LoadedScenes);
            Assert.IsFalse(host.Scenes.HasLoaded(client.PeerId(host), Arena));
            Assert.IsTrue(host.Scenes.HasLoaded(client.PeerId(host), Lobby));
        }

        [Test]
        public void AnUnloadOfTheSceneBeingLoadedRunsAfterTheLoad()
        {
            var host = new Host();
            Client client = host.Connect(manual: true);
            host.Run(20);
            host.Scenes.LoadGlobal(new[] { Arena });
            host.Run(10);

            host.Scenes.UnloadGlobal(new[] { Arena });
            host.Run(10);
            client.Scenes.CompleteNext();
            host.Run(10);

            CollectionAssert.AreEqual(new[] { $"load {Arena:X}", $"unload {Arena:X}" }, client.Log);
            Assert.AreEqual(0, client.Objects.LoadedScenes.Count);
            Assert.IsFalse(host.Scenes.HasLoaded(client.PeerId(host), Arena));
        }

        [Test]
        public void AHostThatLoadsSynchronouslyRunsEveryStepInOrder()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);

            host.Scenes.LoadGlobal(new[] { Arena, Lobby, Room });
            host.Run(10);

            CollectionAssert.AreEqual(new[] { $"load {Arena:X}", $"load {Lobby:X}", $"load {Room:X}" }, client.Log);
            Assert.IsTrue(new[] { Arena, Lobby, Room }.All(sceneId => host.Scenes.HasLoaded(client.PeerId(host), sceneId)));
        }

        [Test]
        public void LoadingAGlobalSceneAgainSendsNothing()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            var added = new List<uint>();
            host.Scenes.OnSceneAdded += added.Add;
            host.Scenes.LoadGlobal(new[] { Arena });
            host.Run(10);

            host.Scenes.LoadGlobal(new[] { Arena });
            host.Run(10);

            CollectionAssert.AreEqual(new[] { Arena }, added);
            CollectionAssert.AreEqual(new[] { $"load {Arena:X}" }, client.Log);
        }

        [Test]
        public void UnloadingAPeerOutsideTheRoomChangesNothing()
        {
            var host = new Host();
            Client member = host.Connect();
            Client outsider = host.Connect();
            host.Run(20);
            var removed = new List<uint>();
            host.Scenes.OnSceneRemoved += removed.Add;
            host.Scenes.LoadForPeers(Room, new[] { member.PeerId(host) });
            host.Run(10);

            Assert.AreEqual(0, host.Scenes.UnloadForPeers(Room, new[] { outsider.PeerId(host) }));
            host.Run(10);

            CollectionAssert.AreEqual(new[] { member.PeerId(host) }, host.Scenes.PeersOf(Room));
            Assert.AreEqual(0, outsider.Log.Count);
            CollectionAssert.AreEqual(new[] { $"load {Room:X}" }, member.Log);
            Assert.AreEqual(0, removed.Count);
        }

        [Test]
        public void AnUnknownSceneStopsTheClientWithPrefabMismatch()
        {
            var host = new Host();
            Client client = host.Connect();
            client.Scenes.Unknown.Add(Arena);
            host.Run(20);

            host.Scenes.LoadGlobal(new[] { Arena });
            host.Run(10);

            Assert.AreEqual(ObjectMismatchKind.UnknownScene, client.Mismatches.Single().Kind);
            Assert.AreEqual(Arena, client.Mismatches.Single().SceneId);
            Assert.AreEqual(StopReason.PrefabMismatch, client.Stopped.Single().Reason);
        }

        [Test]
        public void ALoadThatFailsLaterStopsTheClientWithPrefabMismatch()
        {
            var host = new Host();
            Client client = host.Connect(manual: true);
            host.Run(20);
            host.Scenes.LoadGlobal(new[] { Arena });
            host.Run(10);

            client.Scenes.FailNext();
            host.Run(10);

            Assert.AreEqual(ObjectMismatchKind.UnknownScene, client.Mismatches.Single().Kind);
            Assert.AreEqual(Arena, client.Mismatches.Single().SceneId);
            Assert.AreEqual(StopReason.PrefabMismatch, client.Stopped.Single().Reason);
            Assert.AreEqual(0, client.Objects.LoadedScenes.Count);
        }

        [Test]
        public void ASpawnInASceneTheClientHasNotLoadedIsInvalid()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            var spawn = new ObjectSpawn { ObjectId = 7, PrefabId = PrefabId, SceneId = Arena };

            host.Server.Send(client.PeerId(host), TestObjects.SpawnId, ObjectSpawnNetAdapter.Instance.Encode(spawn).Span);
            host.Run(10);

            Assert.AreEqual(ObjectMismatchKind.InvalidSpawn, client.Mismatches.Single().Kind);
            Assert.AreEqual(StopReason.ObjectMismatch, client.Stopped.Single().Reason);
        }

        [Test]
        public void ALoadedReportForASceneThePeerDoesNotHaveIsIgnored()
        {
            var host = new Host();
            Client client = host.Connect();
            host.Run(20);
            var loaded = new SceneLoaded();
            loaded.Scenes.Add(Room);

            client.Session.Send(TestObjects.SceneLoadedId, SceneLoadedNetAdapter.Instance.Encode(loaded).Span);
            host.Run(10);

            Assert.IsFalse(host.Scenes.HasLoaded(client.PeerId(host), Room));
        }

        [Test]
        public void SceneCallsCheckTheirArguments()
        {
            var host = new Host();
            host.Connect();
            host.Run(20);
            host.Scenes.LoadGlobal(new[] { Arena });

            Assert.Throws<ArgumentException>(() => host.Scenes.LoadGlobal(new[] { 0u }));
            Assert.Throws<ArgumentException>(() => host.Scenes.UnloadGlobal(new[] { Room }));
            Assert.Throws<ArgumentException>(() => host.Scenes.UnloadForPeers(Room, new ulong[0]));
            Assert.Throws<ArgumentException>(() => host.Scenes.Unload(Room));
            Assert.Throws<ArgumentException>(() => host.Objects.Spawn(PrefabId, 0, Room));
            host.Objects.Observes = (objectId, peerId) =>
            {
                host.Scenes.LoadGlobal(new[] { Lobby });
                return true;
            };
            Assert.Throws<InvalidOperationException>(() => host.Objects.Spawn(PrefabId, 0));
        }

        private sealed class Host
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly ServerSession Server;
            public readonly ServerObjects Objects;
            public readonly List<ulong> StartedPeers = new List<ulong>();
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
                Objects = new ServerObjects(Server, TestObjects.Protocol(TestObjects.Channels()), Read, TestObjects.SceneProtocol(TestObjects.Channels()));
                Server.OnRemoteConnectionState += args =>
                {
                    if (args.State == ConnectionState.Started)
                    {
                        StartedPeers.Add(args.PeerId);
                    }
                };
                Server.Start(Listener);
            }

            public ServerScenes Scenes => Objects.Scenes;

            public Client Connect(bool manual = false)
            {
                var client = new Client(this, manual);
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

            private static SpawnData Read(uint objectId) =>
                new SpawnData(0xF1, Vector3.Zero, Quaternion.Identity, Vector3.One, null);
        }

        private sealed class Client
        {
            public readonly ClientSession Session;
            public readonly ClientObjects Objects;
            public readonly List<string> Log = new List<string>();
            public readonly RecordingSpawner Spawner;
            public readonly FakeSceneHost Scenes;
            public readonly List<ObjectMismatchArgs> Mismatches = new List<ObjectMismatchArgs>();
            public readonly List<ConnectionStateArgs> Stopped = new List<ConnectionStateArgs>();
            private readonly int index;

            public Client(Host host, bool manual)
            {
                index = host.Clients.Count;
                Spawner = new RecordingSpawner(Log);
                Scenes = new FakeSceneHost(Log) { Manual = manual };
                Session = new ClientSession(
                    TestObjects.Schema(),
                    new SessionConfig(),
                    new SessionLimits(),
                    new MessageDispatcher(TestObjects.Schema()),
                    TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ClientObjects(Session, TestObjects.Protocol(TestObjects.Channels()), Spawner, TestObjects.SceneProtocol(TestObjects.Channels()), Scenes);
                Objects.OnObjectMismatch += Mismatches.Add;
                Session.OnClientConnectionState += args =>
                {
                    if (args.State == ConnectionState.Stopped)
                    {
                        Stopped.Add(args);
                    }
                };
                Session.Start(host.Listener.Connect(), host.Now);
            }

            public ulong PeerId(Host host) => Objects.LocalPeerId != 0 ? Objects.LocalPeerId : host.StartedPeers[index];
        }

        private sealed class RecordingSpawner : IObjectSpawner
        {
            public readonly List<SpawnedObject> Spawned = new List<SpawnedObject>();
            private readonly List<string> log;

            public RecordingSpawner(List<string> log)
            {
                this.log = log;
            }

            public SpawnResult Spawn(in SpawnedObject spawned)
            {
                Spawned.Add(spawned);
                log.Add($"spawn {spawned.ObjectId}");
                return SpawnResult.Spawned;
            }

            public void Despawn(uint objectId) => log.Add($"despawn {objectId}");
        }

        private sealed class FakeSceneHost : ISceneHost
        {
            public readonly HashSet<uint> Unknown = new HashSet<uint>();
            private readonly List<string> log;
            private readonly List<Action> pending = new List<Action>();
            private readonly List<Action> failures = new List<Action>();

            public FakeSceneHost(List<string> log)
            {
                this.log = log;
            }

            public bool Manual { get; set; }

            public bool TryLoad(uint sceneId, Action loaded, Action failed)
            {
                if (Unknown.Contains(sceneId))
                {
                    return false;
                }

                log.Add($"load {sceneId:X}");
                if (Manual)
                {
                    pending.Add(loaded);
                    failures.Add(failed);
                }
                else
                {
                    loaded();
                }

                return true;
            }

            public void Unload(uint sceneId, Action unloaded)
            {
                log.Add($"unload {sceneId:X}");
                unloaded();
            }

            public void CompleteNext()
            {
                Action loaded = pending[0];
                pending.RemoveAt(0);
                failures.RemoveAt(0);
                loaded();
            }

            public void FailNext()
            {
                Action failed = failures[0];
                pending.RemoveAt(0);
                failures.RemoveAt(0);
                failed();
            }
        }
    }
}
