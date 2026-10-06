using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Tests
{
    public sealed class NetworkScenesTest
    {
        private const uint PrefabId = 0xB3;
        private const uint Arena = 0x5C00_0001;
        private const uint Room = 0x5C00_0002;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private readonly Dictionary<NetworkManager, PreviewLoader> loaders = new Dictionary<NetworkManager, PreviewLoader>();
        private InMemoryNetworkTransport network;
        private NetworkManager server;
        private NetworkObject prefab;
        private TimeSpan now;

        [SetUp]
        public void CreateServer()
        {
            RecordingBehaviour.Clear();
            now = TimeSpan.Zero;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            prefab = TestPrefabs.Create("Prefab", PrefabId);
            server = CreateManager(network);
            server.ServerManager.StartConnection(1);
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (NetworkManager manager in managers)
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
            }

            foreach (PreviewLoader loader in loaders.Values)
            {
                loader.CloseAll();
            }

            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            managers.Clear();
            created.Clear();
            loaders.Clear();
            TestPrefabs.DestroyAllInScene();
            RecordingBehaviour.Clear();
        }

        [Test]
        public void AGlobalSceneLoadsOnBothSidesAndItsSceneObjectsReachTheClient()
        {
            NetworkManager client = Connect();
            var loaded = new List<uint>();
            server.ServerManager.Scenes.OnLoaded += loaded.Add;

            server.ServerManager.Scenes.LoadGlobal(new[] { Arena });
            RunFrames(5);

            Assert.IsTrue(server.ServerManager.Scenes.TryGetScene(Arena, out Scene serverScene));
            NetworkObject onServer = SceneObjectIn(serverScene);
            Assert.IsTrue(onServer.IsSpawned);
            CollectionAssert.AreEqual(new[] { Arena }, loaded);
            NetworkObject onClient = (NetworkObject)client.ClientManager.Spawned[onServer.ObjectId];
            Assert.AreSame(SceneObjectIn(loaders[client].Scenes.Single()), onClient);
            Assert.IsTrue(onClient.gameObject.activeSelf);
            Assert.IsTrue(server.ServerManager.Scenes.HasLoaded(PeerIdOf(client), Arena));
        }

        [Test]
        public void APrefabInsideANetworkSceneArrivesInTheSameSceneOnTheClient()
        {
            NetworkManager client = Connect();
            server.ServerManager.Scenes.LoadGlobal(new[] { Arena });
            RunFrames(5);
            server.ServerManager.Scenes.TryGetScene(Arena, out Scene serverScene);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(instance.gameObject, serverScene);

            server.ServerManager.Spawn(instance);
            RunFrames(3);

            Assert.AreEqual(loaders[client].Scenes.Single(), ((NetworkObject)client.ClientManager.Spawned[instance.ObjectId]).gameObject.scene);
        }

        [Test]
        public void UnloadingAGlobalSceneDespawnsItsObjectsAndUnloadsBothSides()
        {
            NetworkManager client = Connect();
            server.ServerManager.Scenes.LoadGlobal(new[] { Arena });
            RunFrames(5);
            server.ServerManager.Scenes.TryGetScene(Arena, out Scene serverScene);
            uint objectId = SceneObjectIn(serverScene).ObjectId;
            RecordingBehaviour.Log.Clear();

            server.ServerManager.Scenes.UnloadGlobal(new[] { Arena });
            RunFrames(5);

            Assert.IsFalse(server.ServerManager.Spawned.ContainsKey(objectId));
            Assert.IsFalse(client.ClientManager.Spawned.ContainsKey(objectId));
            Assert.IsFalse(server.ServerManager.Scenes.TryGetScene(Arena, out _));
            Assert.AreEqual(1, loaders[server].Closed.Count);
            Assert.AreEqual(1, loaders[client].Closed.Count);
            CollectionAssert.AreEquivalent(new[] { $"{objectId} StopServer", $"{objectId} StopClient" }, RecordingBehaviour.Log);
        }

        [Test]
        public void LoadingASceneThatIsStillUnloadingWaitsForTheUnload()
        {
            NetworkManager client = Connect();
            loaders[server].HoldUnloads = true;
            server.ServerManager.Scenes.LoadGlobal(new[] { Arena });
            RunFrames(5);

            server.ServerManager.Scenes.LoadGlobal(new[] { Arena }, replace: true);
            RunFrames(5);

            Assert.AreEqual(1, loaders[server].Scenes.Count);
            Assert.IsFalse(server.ServerManager.Scenes.TryGetScene(Arena, out _));

            loaders[server].FinishUnloads();
            RunFrames(5);

            Assert.AreEqual(2, loaders[server].Scenes.Count);
            Assert.IsTrue(server.ServerManager.Scenes.TryGetScene(Arena, out Scene reloaded));
            Assert.AreEqual(loaders[server].Scenes[1], reloaded);
            NetworkObject sceneObject = SceneObjectIn(reloaded);
            Assert.IsTrue(sceneObject.IsSpawned);
            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(sceneObject.ObjectId));
        }

        [Test]
        public void UnloadingAgainBeforeTheUnloadFinishesCancelsTheReload()
        {
            loaders[server].HoldUnloads = true;
            server.ServerManager.Scenes.LoadGlobal(new[] { Arena });
            RunFrames(3);
            server.ServerManager.Scenes.UnloadGlobal(new[] { Arena });
            server.ServerManager.Scenes.LoadGlobal(new[] { Arena });
            server.ServerManager.Scenes.UnloadGlobal(new[] { Arena });

            loaders[server].FinishUnloads();
            RunFrames(3);

            Assert.AreEqual(1, loaders[server].Scenes.Count);
            Assert.IsFalse(server.ServerManager.Scenes.TryGetScene(Arena, out _));
        }

        [Test]
        public void ASceneMissingFromTheRegistryIsRefused()
        {
            Assert.Throws<ArgumentException>(() => server.ServerManager.Scenes.LoadGlobal(new[] { 0x5C00_0099u }));
            Assert.Throws<ArgumentException>(() => server.ServerManager.Scenes.LoadForPeers(0x5C00_0099u, new ulong[0]));
            Assert.IsFalse(server.ServerManager.Scenes.Contains(0x5C00_0099u));
        }

        [Test]
        public void AServerLoadFailureIsReportedAndSpawnsNothing()
        {
            loaders[server].Fail = true;
            var failures = new List<uint>();
            server.ServerManager.Scenes.OnLoadFailed += (sceneId, exception) => failures.Add(sceneId);
            LogAssert.Expect(LogType.Exception, new Regex("preview load failed"));

            server.ServerManager.Scenes.LoadGlobal(new[] { Arena });
            RunFrames(3);

            CollectionAssert.AreEqual(new[] { Arena }, failures);
            Assert.IsTrue(server.ServerManager.Scenes.Contains(Arena));
            Assert.IsFalse(server.ServerManager.Scenes.TryGetScene(Arena, out _));
            Assert.AreEqual(0, server.ServerManager.Spawned.Count);
        }

        [Test]
        public void AClientLoadFailureStopsItWithPrefabMismatch()
        {
            NetworkManager client = Connect();
            loaders[client].Fail = true;
            var stops = new List<ConnectionStateArgs>();
            client.ClientManager.OnClientConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    stops.Add(args);
                }
            };
            LogAssert.Expect(LogType.Exception, new Regex("preview load failed"));
            LogAssert.Expect(LogType.Error, new Regex("UnknownScene"));

            server.ServerManager.Scenes.LoadGlobal(new[] { Arena });
            RunFrames(5);

            Assert.AreEqual(StopReason.PrefabMismatch, stops.Single().Reason);
            Assert.IsFalse(stops.Single().WillRetry);
        }

        [Test]
        public void AStoppedServerUnloadsItsScenesAndARestartSkipsScenesStillUnloading()
        {
            loaders[server].HoldUnloads = true;
            server.ServerManager.Scenes.LoadGlobal(new[] { Arena });
            RunFrames(3);
            server.ServerManager.Scenes.TryGetScene(Arena, out Scene serverScene);
            NetworkObject sceneObject = SceneObjectIn(serverScene);

            server.ServerManager.StopConnection();
            Assert.AreEqual(1, loaders[server].Unloading.Count);
            server.FindServerSceneObjects = () => new List<NetworkObject> { sceneObject };
            server.ServerManager.StartConnection(1);

            Assert.IsFalse(sceneObject.IsSpawned);
            loaders[server].FinishUnloads();
            Assert.AreEqual(1, loaders[server].Closed.Count);
        }

        [Test]
        public void TheHostClientUsesTheServerScene()
        {
            var hostNetwork = new GameObject("HostNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(hostNetwork.gameObject);
            NetworkManager host = CreateManager(hostNetwork);
            host.ServerManager.StartConnection(2);
            host.ClientManager.StartConnection("unused.invalid", 2);
            RunFrames(20);

            host.ServerManager.Scenes.LoadGlobal(new[] { Arena });
            RunFrames(5);

            host.ServerManager.Scenes.TryGetScene(Arena, out Scene scene);
            NetworkObject shared = SceneObjectIn(scene);
            Assert.AreEqual(1, loaders[host].Scenes.Count);
            Assert.AreSame(shared, host.ClientManager.Spawned[shared.ObjectId]);
            Assert.IsTrue(host.ServerManager.Scenes.HasLoaded(host.ClientManager.Objects.LocalPeerId, Arena));
        }

        [Test]
        public void ARoomReachesOnlyItsPeer()
        {
            NetworkManager member = Connect();
            NetworkManager other = Connect();

            Assert.AreEqual(1, server.ServerManager.Scenes.LoadForPeers(Room, new[] { PeerIdOf(member) }));
            RunFrames(5);

            server.ServerManager.Scenes.TryGetScene(Room, out Scene serverScene);
            uint objectId = SceneObjectIn(serverScene).ObjectId;
            Assert.IsTrue(member.ClientManager.Spawned.ContainsKey(objectId));
            Assert.IsFalse(other.ClientManager.Spawned.ContainsKey(objectId));
            Assert.AreEqual(0, loaders[other].Scenes.Count);
            CollectionAssert.AreEqual(new[] { PeerIdOf(member) }, server.ServerManager.Scenes.PeersOf(Room));
        }

        private static NetworkObject SceneObjectIn(Scene scene) =>
            scene.GetRootGameObjects().Select(root => root.GetComponent<NetworkObject>()).Single(networkObject => networkObject != null);

        private ulong PeerIdOf(NetworkManager client) => client.ClientManager.Objects.LocalPeerId;

        private NetworkManager Connect()
        {
            NetworkManager client = CreateManager(network);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            return client;
        }

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
            manager.ClientManager.FindSceneObjects = () => new List<NetworkObject>();
            manager.Prefabs.Register(prefab);
            var loader = new PreviewLoader();
            manager.Scenes.Register(Arena, loader.For(Arena));
            manager.Scenes.Register(Room, loader.For(Room));
            loaders.Add(manager, loader);
            created.Add(manager.gameObject);
            managers.Add(manager);
            return manager;
        }

        private void RunFrames(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                foreach (NetworkManager manager in managers)
                {
                    manager.RunFrameStart(FrameSeconds, now);
                }

                foreach (NetworkManager manager in managers)
                {
                    manager.RunFrameEnd();
                }
            }
        }

        private sealed class PreviewLoader
        {
            public readonly List<Scene> Scenes = new List<Scene>();
            public readonly List<Scene> Closed = new List<Scene>();
            public readonly List<Action> Unloading = new List<Action>();

            public bool Fail { get; set; }

            public bool HoldUnloads { get; set; }

            public ISceneLoader For(uint sceneId) => new Loader(this, sceneId);

            public void FinishUnloads()
            {
                List<Action> pending = Unloading.ToList();
                Unloading.Clear();
                foreach (Action unload in pending)
                {
                    unload();
                }
            }

            public void CloseAll()
            {
                FinishUnloads();
                foreach (Scene scene in Scenes)
                {
                    if (!Closed.Contains(scene))
                    {
                        EditorSceneManager.ClosePreviewScene(scene);
                    }
                }
            }

            private sealed class Loader : ISceneLoader
            {
                private readonly PreviewLoader owner;
                private readonly uint sceneId;

                public Loader(PreviewLoader owner, uint sceneId)
                {
                    this.owner = owner;
                    this.sceneId = sceneId;
                }

                public void Load(Action<Scene> loaded, Action<Exception> failed)
                {
                    if (owner.Fail)
                    {
                        failed(new InvalidOperationException("preview load failed"));
                        return;
                    }

                    Scene scene = EditorSceneManager.NewPreviewScene();
                    NetworkObject sceneObject = TestPrefabs.CreateSceneObject("Crate", ((ulong)sceneId << 32) | 1);
                    SceneManager.MoveGameObjectToScene(sceneObject.gameObject, scene);
                    owner.Scenes.Add(scene);
                    loaded(scene);
                }

                public void Unload(Scene scene, Action unloaded)
                {
                    void Close()
                    {
                        EditorSceneManager.ClosePreviewScene(scene);
                        owner.Closed.Add(scene);
                        unloaded();
                    }

                    if (owner.HoldUnloads)
                    {
                        owner.Unloading.Add(Close);
                        return;
                    }

                    Close();
                }
            }
        }
    }
}
