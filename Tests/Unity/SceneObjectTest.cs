using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Tests
{
    public sealed class SceneObjectTest
    {
        private const ulong DoorId = 0x1234_5678_0000_0001;
        private const ulong CrateId = 0x1234_5678_0000_0002;
        private const uint PrefabId = 0xA1;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkObject> serverScene = new List<NetworkObject>();
        private readonly List<NetworkObject> clientScene = new List<NetworkObject>();
        private NetworkManager server;
        private NetworkManager client;
        private TimeSpan now;

        [SetUp]
        public void CreateManagers()
        {
            RecordingBehaviour.Clear();
            now = TimeSpan.Zero;
            var network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            server = CreateManager(network);
            client = CreateManager(network);
            server.ServerManager.FindSceneObjects = () => serverScene;
            client.ClientManager.FindSceneObjects = () => clientScene;
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (NetworkManager manager in new[] { client, server })
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
            }

            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            created.Clear();
            serverScene.Clear();
            clientScene.Clear();
            TestPrefabs.DestroyAllInScene();
            RecordingBehaviour.Clear();
        }

        [Test]
        public void ServerStartSpawnsEverySceneObjectIncludingInactiveOnes()
        {
            NetworkObject door = SceneObject(serverScene, "Door", DoorId);
            NetworkObject crate = SceneObject(serverScene, "Crate", CrateId);
            crate.gameObject.SetActive(false);

            server.ServerManager.StartConnection(1);

            Assert.AreEqual(1u, door.ObjectId);
            Assert.AreEqual(2u, crate.ObjectId);
            Assert.IsTrue(crate.gameObject.activeSelf);
            Assert.AreSame(crate, server.ServerManager.Spawned[2]);
            Assert.AreEqual(new[] { "1 StartServer", "2 StartServer" }, RecordingBehaviour.Log);
        }

        [Test]
        public void ClientPlacesItsSceneObjectAtTheServerTransformAndHidesTheOthers()
        {
            NetworkObject serverDoor = SceneObject(serverScene, "ServerDoor", DoorId);
            NetworkObject clientDoor = SceneObject(clientScene, "ClientDoor", DoorId);
            NetworkObject clientOnly = SceneObject(clientScene, "ClientOnly", CrateId);
            server.ServerManager.StartConnection(1);
            serverDoor.transform.position = new Vector3(5, 0, 0);

            Connect();

            Assert.AreSame(clientDoor, client.ClientManager.Spawned[serverDoor.ObjectId]);
            Assert.AreEqual(new Vector3(5, 0, 0), clientDoor.transform.position);
            Assert.IsTrue(clientDoor.gameObject.activeSelf);
            Assert.IsFalse(clientOnly.gameObject.activeSelf);
            Assert.IsFalse(clientOnly.IsSpawned);
            Assert.AreEqual(3, TestPrefabs.CountInScene());
            Assert.AreEqual(new[] { "1 StartServer", "1 StartClient" }, RecordingBehaviour.Log);
        }

        [Test]
        public void DespawnDisablesTheSceneObjectOnBothSidesAndItSpawnsAgain()
        {
            NetworkObject serverDoor = SceneObject(serverScene, "ServerDoor", DoorId);
            NetworkObject clientDoor = SceneObject(clientScene, "ClientDoor", DoorId);
            server.ServerManager.StartConnection(1);
            Connect();

            Assert.IsTrue(server.ServerManager.Despawn(serverDoor));
            RunFrames(3);

            Assert.IsFalse(serverDoor == null);
            Assert.IsFalse(serverDoor.gameObject.activeSelf);
            Assert.IsFalse(serverDoor.IsSpawned);
            Assert.IsFalse(clientDoor.gameObject.activeSelf);
            Assert.IsFalse(clientDoor.IsSpawned);
            server.ServerManager.Spawn(serverDoor);
            RunFrames(3);
            Assert.AreEqual(2u, serverDoor.ObjectId);
            Assert.IsTrue(serverDoor.gameObject.activeSelf);
            Assert.AreSame(clientDoor, client.ClientManager.Spawned[2]);
            Assert.IsTrue(clientDoor.gameObject.activeSelf);
            Assert.AreEqual(
                new[] { "1 StartServer", "1 StartClient", "1 StopServer", "1 StopClient", "2 StartServer", "2 StartClient" },
                RecordingBehaviour.Log);
        }

        [Test]
        public void ServerStopDisablesSceneObjectsAndARestartSpawnsThemAgain()
        {
            NetworkObject serverDoor = SceneObject(serverScene, "ServerDoor", DoorId);
            NetworkObject clientDoor = SceneObject(clientScene, "ClientDoor", DoorId);
            server.ServerManager.StartConnection(1);
            Connect();

            server.ServerManager.StopConnection();
            RunFrames(5);

            Assert.IsFalse(serverDoor.gameObject.activeSelf);
            Assert.IsFalse(serverDoor.IsSpawned);
            Assert.IsFalse(clientDoor.gameObject.activeSelf);
            Assert.IsFalse(clientDoor == null);
            server.ServerManager.StartConnection(1);
            Assert.IsTrue(serverDoor.gameObject.activeSelf);
            Assert.AreEqual(1u, serverDoor.ObjectId);
        }

        [Test]
        public void ClientStopDisablesItsSceneObjects()
        {
            SceneObject(serverScene, "ServerDoor", DoorId);
            NetworkObject clientDoor = SceneObject(clientScene, "ClientDoor", DoorId);
            server.ServerManager.StartConnection(1);
            Connect();
            RecordingBehaviour.Log.Clear();

            client.ClientManager.StopConnection();

            Assert.AreEqual(new[] { "1 StopClient" }, RecordingBehaviour.Log);
            Assert.IsFalse(clientDoor == null);
            Assert.IsFalse(clientDoor.gameObject.activeSelf);
            Assert.AreEqual(0, client.ClientManager.Spawned.Count);
        }

        [Test]
        public void MissingSceneObjectStopsTheClientWithPrefabMismatch()
        {
            SceneObject(serverScene, "ServerDoor", DoorId);
            var mismatches = new List<ObjectMismatchArgs>();
            client.ClientManager.OnObjectMismatch += mismatches.Add;
            var states = new List<ConnectionStateArgs>();
            client.ClientManager.OnClientConnectionState += states.Add;
            server.ServerManager.StartConnection(1);

            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);

            Assert.AreEqual(ObjectMismatchKind.UnknownSceneObject, mismatches[0].Kind);
            Assert.AreEqual(DoorId, mismatches[0].SceneObjectId);
            Assert.AreEqual(0u, mismatches[0].PrefabId);
            Assert.AreEqual(StopReason.PrefabMismatch, states[states.Count - 1].Reason);
        }

        [Test]
        public void SceneObjectWithOtherBehavioursStopsTheClientWithPrefabMismatch()
        {
            SceneObject(serverScene, "ServerDoor", DoorId);
            NetworkObject clientDoor = SceneObject(clientScene, "ClientDoor", DoorId);
            clientDoor.gameObject.AddComponent<SilentBehaviour>();
            var mismatches = new List<ObjectMismatchArgs>();
            client.ClientManager.OnObjectMismatch += mismatches.Add;
            server.ServerManager.StartConnection(1);

            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);

            Assert.AreEqual(ObjectMismatchKind.IncompatibleSceneObject, mismatches[0].Kind);
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
        }

        [Test]
        public void MismatchLogNamesTheSceneObject()
        {
            SceneObject(serverScene, "ServerDoor", DoorId);
            server.ServerManager.StartConnection(1);

            LogAssert.Expect(LogType.Error, new Regex("object mismatch UnknownSceneObject, object 1, prefab 0x00000000, scene object 0x1234567800000001"));
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);

            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
        }

        [Test]
        public void RuntimeCopyOfASceneObjectIsSpawnedFromItsPrefab()
        {
            NetworkObject prefab = TestPrefabs.Create("Prefab", PrefabId);
            server.Prefabs.Register(prefab);
            client.Prefabs.Register(prefab);
            NetworkObject serverDoor = SceneObject(serverScene, "ServerDoor", DoorId, PrefabId);
            NetworkObject clientDoor = SceneObject(clientScene, "ClientDoor", DoorId, PrefabId);
            server.ServerManager.StartConnection(1);
            Connect();
            NetworkObject copy = UnityEngine.Object.Instantiate(serverDoor);

            server.ServerManager.Spawn(copy);
            RunFrames(3);

            Assert.AreEqual(DoorId, copy.SceneObjectId);
            Assert.AreEqual(2u, copy.ObjectId);
            NetworkObject remoteCopy = (NetworkObject)client.ClientManager.Spawned[2];
            Assert.AreNotSame(clientDoor, remoteCopy);
            Assert.AreSame(clientDoor, client.ClientManager.Spawned[1]);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        [Test]
        public void InvalidSceneObjectIsLoggedAndTheOthersStillSpawn()
        {
            NetworkObject outer = SceneObject(serverScene, "Outer", CrateId);
            NetworkObject inner = SceneObject(serverScene, "Inner", CrateId + 1);
            inner.transform.SetParent(outer.transform);
            NetworkObject door = SceneObject(serverScene, "Door", DoorId);

            LogAssert.Expect(LogType.Exception, new Regex("nested"));
            LogAssert.Expect(LogType.Exception, new Regex("nested"));
            server.ServerManager.StartConnection(1);

            Assert.IsFalse(outer.IsSpawned);
            Assert.IsFalse(inner.IsSpawned);
            Assert.IsTrue(door.IsSpawned);
            Assert.AreEqual(ServerState.Started, server.ServerManager.State);
        }

        [Test]
        public void StartServerExceptionIsLoggedAndTheObjectStaysSpawned()
        {
            NetworkObject door = SceneObject(serverScene, "Door", DoorId);
            NetworkObject crate = SceneObject(serverScene, "Crate", CrateId);
            RecordingBehaviour.ThrowOn = "StartServer";

            LogAssert.Expect(LogType.Exception, new Regex("StartServer failed"));
            LogAssert.Expect(LogType.Exception, new Regex("StartServer failed"));
            server.ServerManager.StartConnection(1);

            Assert.IsTrue(door.IsSpawned);
            Assert.IsTrue(crate.IsSpawned);
        }

        [Test]
        public void HostClientUsesTheServerSceneObjects()
        {
            NetworkManager host = CreateManager(host: true);
            var hostScene = new List<NetworkObject>();
            host.ServerManager.FindSceneObjects = () => hostScene;
            host.ClientManager.FindSceneObjects = () => hostScene;
            NetworkObject door = SceneObject(hostScene, "Door", DoorId);
            host.ServerManager.StartConnection(1);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20, host);

            Assert.AreSame(door, host.ClientManager.Spawned[1]);
            Assert.IsTrue(door.gameObject.activeSelf);
            Assert.AreEqual(new[] { "1 StartServer", "1 StartClient" }, RecordingBehaviour.Log);
            host.ServerManager.Despawn(door);
            RunFrames(3, host);
            Assert.AreEqual(new[] { "1 StartServer", "1 StartClient", "1 StopClient", "1 StopServer" }, RecordingBehaviour.Log);
            Assert.IsFalse(door.gameObject.activeSelf);
            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
            host.ClientManager.StopConnection();
            host.ServerManager.StopConnection();
        }

        private NetworkObject SceneObject(List<NetworkObject> scene, string name, ulong sceneObjectId, uint prefabId = 0)
        {
            NetworkObject sceneObject = TestPrefabs.CreateSceneObject(name, sceneObjectId, prefabId);
            scene.Add(sceneObject);
            return sceneObject;
        }

        private NetworkManager CreateManager(NetworkTransport transport = null, bool host = false)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = host ? manager.gameObject.AddComponent<InMemoryNetworkTransport>() : transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            created.Add(manager.gameObject);
            return manager;
        }

        private void Connect()
        {
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        private void RunFrames(int count, params NetworkManager[] managers)
        {
            NetworkManager[] running = managers.Length == 0 ? new[] { server, client } : managers;
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                foreach (NetworkManager manager in running)
                {
                    manager.RunFrameStart(FrameSeconds, now);
                }

                foreach (NetworkManager manager in running)
                {
                    manager.RunFrameEnd();
                }
            }
        }
    }
}
