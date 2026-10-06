using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Tests
{
    public sealed class PredictionInputTest
    {
        private const uint PrefabId = 0xB6;
        private const uint TwoInputsPrefabId = 0xB7;
        private const double FrameSeconds = 1.0 / 60;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private InMemoryNetworkTransport network;
        private NetworkObject prefab;
        private NetworkObject twoInputsPrefab;
        private TimeSpan now;

        [SetUp]
        public void CreateNetwork()
        {
            now = TimeSpan.FromSeconds(1);
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            prefab = TestPrefabs.Create("Input", PrefabId);
            prefab.gameObject.AddComponent<InputBehaviour>();
            twoInputsPrefab = TestPrefabs.Create("TwoInputs", TwoInputsPrefabId);
            twoInputsPrefab.gameObject.AddComponent<TwoInputBehaviour>();
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (NetworkManager manager in managers)
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
            }

            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            managers.Clear();
            created.Clear();
            TestPrefabs.DestroyAllInScene();
        }

        [Test]
        public void TheOwnerGathersEveryTickAndTheServerAppliesTheInputOfThatTick()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            InputBehaviour onServer = SpawnOwnedBy(server, clientId);
            InputBehaviour onClient = Remote(client, onServer);
            RunFrames(60);

            Assert.Greater(onClient.Gathered, 30);
            Assert.Greater(onServer.Applied.Count, 30);
            Assert.IsTrue(onServer.Applied.All(applied => !applied.Repeated));
            for (int index = 1; index < onServer.Applied.Count; index++)
            {
                Assert.AreEqual(onServer.Applied[index - 1].Tick + 1, onServer.Applied[index].Tick);
                Assert.AreEqual(onServer.Applied[index - 1].Step + 1, onServer.Applied[index].Step);
            }

            Assert.IsTrue(client.ClientManager.Clock.HasInputLead);
            Assert.AreEqual(0, server.ServerManager.DroppedInputsOf(clientId));
            Assert.IsEmpty(onClient.Applied);
        }

        [Test]
        public void WhenTheOwnerFallsSilentTheServerRepeatsWithAGrowingRepeatedTicks()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            InputBehaviour onServer = SpawnOwnedBy(server, clientId);
            RunFrames(30);
            int before = onServer.Applied.Count;
            managers.Remove(client);
            RunFrames(10);

            List<AppliedInput> silent = onServer.Applied.Skip(before).Where(applied => applied.Repeated).ToList();
            Assert.GreaterOrEqual(silent.Count, 5);
            for (int index = 1; index < silent.Count; index++)
            {
                Assert.AreEqual(silent[index - 1].RepeatedTicks + 1, silent[index].RepeatedTicks);
                Assert.AreEqual(silent[index - 1].Step, silent[index].Step);
            }

            Assert.IsTrue(onServer.Applied.Take(before).All(applied => applied.RepeatedTicks == 0));
            managers.Add(client);
        }

        [Test]
        public void TheHostGathersAndAppliesItsOwnInputInTheSameTick()
        {
            NetworkManager host = StartServer();
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            InputBehaviour shared = SpawnOwnedBy(host, host.ClientManager.Objects.LocalPeerId);
            int gatheredBefore = shared.Gathered;
            int appliedBefore = shared.Applied.Count;
            RunFrames(30);

            Assert.AreEqual(shared.Gathered - gatheredBefore, shared.Applied.Count - appliedBefore);
            Assert.AreEqual(host.TimeManager.Tick, shared.Applied[shared.Applied.Count - 1].Tick);
            Assert.AreEqual(shared.NextStep, shared.Applied[shared.Applied.Count - 1].Step);
            Assert.IsFalse(shared.Applied.Any(applied => applied.Repeated));
        }

        [Test]
        public void AnObjectWithoutAnOwnerHasNoInput()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong _) = Connect(server);
            InputBehaviour onServer = SpawnOwnedBy(server, 0);
            RunFrames(30);

            Assert.IsEmpty(onServer.Applied);
            Assert.AreEqual(0, Remote(client, onServer).Gathered);
        }

        [Test]
        public void AfterAnOwnerChangeOnlyTheNewOwnerDrivesTheObject()
        {
            NetworkManager server = StartServer();
            (NetworkManager first, ulong firstId) = Connect(server);
            (NetworkManager second, ulong secondId) = Connect(server);
            InputBehaviour onServer = SpawnOwnedBy(server, firstId);
            Remote(first, onServer).Tag = 1000;
            Remote(second, onServer).Tag = 2000;
            RunFrames(30);
            Assert.IsTrue(onServer.Applied.Any(applied => applied.Step > 1000 && applied.Step < 2000));

            Assert.IsTrue(server.ServerManager.ChangeOwner(onServer.NetworkObject, secondId));
            int appliedAtChange = onServer.Applied.Count;
            RunFrames(5);
            int firstGathered = Remote(first, onServer).Gathered;
            RunFrames(30);

            List<AppliedInput> afterChange = onServer.Applied.Skip(appliedAtChange).ToList();
            Assert.IsNotEmpty(afterChange);
            Assert.IsTrue(afterChange.All(applied => applied.Step > 2000));
            Assert.AreEqual(firstGathered, Remote(first, onServer).Gathered);
        }

        [Test]
        public void InputFromAPeerThatDoesNotOwnTheObjectIsDroppedWithAWarning()
        {
            NetworkManager server = StartServer();
            (NetworkManager owner, ulong ownerId) = Connect(server);
            (NetworkManager other, ulong otherId) = Connect(server);
            InputBehaviour onServer = SpawnOwnedBy(server, ownerId);
            var frames = new InputFrames { Tick = server.TimeManager.Tick + 2 };
            frames.Frames.Add(new byte[] { 9, 0, 0, 0 });

            LogAssert.Expect(LogType.Warning, new Regex($"dropped input to object {onServer.NetworkObject.ObjectId} from peer {otherId}, which does not own the object"));
            other.ClientManager.SendToObject(TestObjects.InputFramesId, onServer.NetworkObject.ObjectId, onServer.BehaviourIndex, InputFramesNetAdapter.Instance.Encode(frames).Span);
            RunFrames(5);

            Assert.IsFalse(onServer.Applied.Any(applied => applied.Step == 9));
        }

        [Test]
        public void AnExceptionFromApplyIsLoggedAndTheTickGoesOn()
        {
            NetworkManager host = StartServer();
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            InputBehaviour shared = SpawnOwnedBy(host, host.ClientManager.Objects.LocalPeerId);
            shared.Tag = InputBehaviour.ThrowingStep - shared.NextStep - 1;
            int appliedBefore = shared.Applied.Count;

            LogAssert.Expect(LogType.Exception, new Regex("apply failed"));
            RunFrames(3);

            Assert.AreEqual(appliedBefore + 2, shared.Applied.Count);
        }

        [Test]
        public void RegisteringInputInTheVariableModeOrTwiceThrows()
        {
            NetworkManager variable = CreateManager(TimingMode.Variable);
            variable.ServerManager.StartConnection(1);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);

            HandlerRegistrationException variableError = Assert.Throws<HandlerRegistrationException>(() => variable.ServerManager.Spawn(instance));

            StringAssert.Contains("Variable timing mode", variableError.Message);
            variable.ServerManager.StopConnection();
            NetworkManager tick = StartServer();
            NetworkObject twice = UnityEngine.Object.Instantiate(twoInputsPrefab);

            HandlerRegistrationException twiceError = Assert.Throws<HandlerRegistrationException>(() => tick.ServerManager.Spawn(twice));

            StringAssert.Contains("uses more than one input model", twiceError.Message);
        }

        private NetworkManager StartServer()
        {
            NetworkManager server = CreateManager(TimingMode.Tick);
            server.ServerManager.StartConnection(1);
            return server;
        }

        private (NetworkManager Manager, ulong PeerId) Connect(NetworkManager server)
        {
            ulong peerId = 0;
            Action<ConnectionStateArgs> record = args => peerId = args.PeerId;
            server.ServerManager.OnRemoteConnectionState += record;
            NetworkManager client = CreateManager(TimingMode.Tick);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            server.ServerManager.OnRemoteConnectionState -= record;
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            return (client, peerId);
        }

        private InputBehaviour SpawnOwnedBy(NetworkManager server, ulong ownerId)
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance, ownerId);
            RunFrames(3);
            return instance.GetComponent<InputBehaviour>();
        }

        private static InputBehaviour Remote(NetworkManager client, InputBehaviour onServer) =>
            ((NetworkObject)client.ClientManager.Spawned[onServer.NetworkObject.ObjectId]).GetComponent<InputBehaviour>();

        private NetworkManager CreateManager(TimingMode mode)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = network;
            serialized.FindProperty("tickRate").intValue = 60;
            serialized.FindProperty("timingMode").enumValueIndex = (int)mode;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
            manager.FindClientSceneObjects = () => new List<NetworkObject>();
            manager.Prefabs.Register(prefab);
            manager.Prefabs.Register(twoInputsPrefab);
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
    }
}
