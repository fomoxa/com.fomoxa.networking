using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Tests
{
    public sealed class NetworkAnimatorTest
    {
        private const uint PrefabId = 0xF1;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private InMemoryNetworkTransport network;
        private NetworkManager server;
        private NetworkObject prefab;
        private NetworkObject otherControllerPrefab;
        private TimeSpan now;

        [SetUp]
        public void CreateServer()
        {
            RecordingBehaviour.Clear();
            now = TimeSpan.Zero;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            prefab = Animated("Animated", Controller(false));
            otherControllerPrefab = Animated("OtherController", Controller(true));
            server = CreateManager(network, prefab);
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

            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            managers.Clear();
            created.Clear();
            TestPrefabs.DestroyAllInScene();
            RecordingBehaviour.Clear();
        }

        [Test]
        public void ParametersAndSpeedReachTheClientWithFloatsRoundedToTheStep()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkAnimator onServer = Instantiate();
            onServer.Animator.SetFloat("Speed", 0.123f);
            onServer.Animator.SetInteger("Count", 3);
            onServer.Animator.SetBool("Running", true);
            onServer.Animator.speed = 1.5f;

            server.ServerManager.Spawn(onServer.GetComponent<NetworkObject>());
            RunFrames(3);
            Animator onClient = Remote(client, onServer).Animator;
            float initialSpeed = onClient.GetFloat("Speed");
            onServer.Animator.SetFloat("Speed", 0.5f);
            onServer.Animator.SetInteger("Count", 4);
            RunFrames(3);

            Assert.AreEqual(0.12f, initialSpeed, 0.00001f);
            Assert.AreEqual(0.5f, onClient.GetFloat("Speed"), 0.00001f);
            Assert.AreEqual(4, onClient.GetInteger("Count"));
            Assert.IsTrue(onClient.GetBool("Running"));
            Assert.AreEqual(1.5f, onClient.speed);
        }

        [Test]
        public void FloatChangeSmallerThanHalfAStepChangesNoStateByte()
        {
            Connect();
            NetworkAnimator onServer = Instantiate();
            onServer.Animator.SetFloat("Speed", 0.12f);
            server.ServerManager.Spawn(onServer.GetComponent<NetworkObject>());
            RunFrames(3);
            byte[] before = onServer.StateSlot.Sent.ToArray();

            onServer.Animator.SetFloat("Speed", 0.1204f);
            RunFrames(3);

            Assert.AreEqual(before, onServer.StateSlot.Sent.ToArray());
        }

        [Test]
        public void ServerTriggerFiresOnTheClientButTheSpawnFiresNone()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkAnimator onServer = Instantiate();
            onServer.SetTrigger("Jump");
            server.ServerManager.Spawn(onServer.GetComponent<NetworkObject>());
            RunFrames(3);
            Animator onClient = Remote(client, onServer).Animator;
            bool afterSpawn = onClient.GetBool("Jump");

            onServer.SetTrigger("Jump");
            RunFrames(3);

            Assert.IsFalse(afterSpawn);
            Assert.IsTrue(onClient.GetBool("Jump"));
            Assert.AreEqual(2, onServer.State.Triggers[0]);
        }

        [Test]
        public void ClientTriggerIsLocalOnly()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkAnimator onServer = Instantiate();
            server.ServerManager.Spawn(onServer.GetComponent<NetworkObject>());
            RunFrames(3);
            NetworkAnimator onClient = Remote(client, onServer);

            onClient.SetTrigger("Jump");
            RunFrames(3);

            Assert.IsTrue(onClient.Animator.GetBool("Jump"));
            Assert.AreEqual(0, onClient.State.Triggers[0]);
            Assert.IsFalse(onServer.Animator.GetBool("Jump"));
        }

        [Test]
        public void LateClientStartsInTheLayerStateOfTheServer()
        {
            NetworkAnimator onServer = Instantiate();
            server.ServerManager.Spawn(onServer.GetComponent<NetworkObject>());
            onServer.Animator.SetBool("Running", true);
            for (int step = 0; step < 3; step++)
            {
                onServer.Animator.Update(0.1f);
            }

            RunFrames(3);

            (NetworkManager late, ulong _) = Connect();
            Animator onLate = Remote(late, onServer).Animator;
            onLate.Update(0f);

            Assert.AreEqual(Animator.StringToHash("Base.Run"), onServer.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash);
            Assert.AreEqual(Animator.StringToHash("Base.Run"), onLate.GetCurrentAnimatorStateInfo(0).fullPathHash);
        }

        [Test]
        public void DifferentControllerIsReportedOnceAndNothingIsApplied()
        {
            (NetworkManager client, ulong _) = Connect(otherControllerPrefab);
            NetworkAnimator onServer = Instantiate();
            onServer.Animator.SetFloat("Speed", 0.3f);

            LogAssert.Expect(LogType.Error, new Regex("Animator controllers differ between server and client"));
            server.ServerManager.Spawn(onServer.GetComponent<NetworkObject>());
            RunFrames(3);
            onServer.Animator.SetFloat("Speed", 0.6f);
            RunFrames(3);

            Assert.AreEqual(0f, Remote(client, onServer).Animator.GetFloat("Speed"));
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void HostClientDoesNotApplyTheStateItReceives()
        {
            var hostNetwork = new GameObject("HostNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(hostNetwork.gameObject);
            NetworkManager host = CreateManager(hostNetwork, prefab);
            host.ServerManager.StartConnection(2);
            host.ClientManager.StartConnection("unused.invalid", 2);
            RunFrames(20);
            NetworkAnimator shared = Instantiate();
            host.ServerManager.Spawn(shared.GetComponent<NetworkObject>());
            RunFrames(3);

            shared.Animator.SetFloat("Speed", 0.3f);
            RunFrames(1);
            shared.Animator.SetFloat("Speed", 0.6f);
            RunFrames(5);

            Assert.AreEqual(0.6f, shared.Animator.GetFloat("Speed"), 0.00001f);
            Assert.AreEqual(0.6f, shared.State.Floats[0], 0.00001f);
        }

        [Test]
        public void TriggerThatTheControllerDoesNotHaveThrows()
        {
            NetworkAnimator onServer = Instantiate();

            Assert.Throws<ArgumentException>(() => onServer.SetTrigger("Fly"));
        }

        private static AnimatorController Controller(bool extraParameter)
        {
            var controller = new AnimatorController();
            controller.AddLayer("Base");
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Count", AnimatorControllerParameterType.Int);
            controller.AddParameter("Running", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            if (extraParameter)
            {
                controller.AddParameter("Extra", AnimatorControllerParameterType.Float);
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            UnityEditor.Animations.AnimatorState idle = machine.AddState("Idle");
            UnityEditor.Animations.AnimatorState run = machine.AddState("Run");
            machine.defaultState = idle;
            AnimatorStateTransition toRun = idle.AddTransition(run);
            toRun.AddCondition(AnimatorConditionMode.If, 0, "Running");
            toRun.hasExitTime = false;
            toRun.duration = 0;
            return controller;
        }

        private NetworkObject Animated(string name, AnimatorController controller)
        {
            NetworkObject networkObject = TestPrefabs.Create(name, PrefabId);
            networkObject.gameObject.AddComponent<NetworkAnimator>().Animator.runtimeAnimatorController = controller;
            return networkObject;
        }

        private NetworkAnimator Instantiate()
        {
            NetworkAnimator instance = UnityEngine.Object.Instantiate(prefab).GetComponent<NetworkAnimator>();
            instance.Animator.Rebind();
            return instance;
        }

        private (NetworkManager Manager, ulong PeerId) Connect(NetworkObject clientPrefab = null)
        {
            ulong peerId = 0;
            Action<ConnectionStateArgs> record = args => peerId = args.PeerId;
            server.ServerManager.OnRemoteConnectionState += record;
            NetworkManager client = CreateManager(network, clientPrefab ?? prefab);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            server.ServerManager.OnRemoteConnectionState -= record;
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            return (client, peerId);
        }

        private static NetworkAnimator Remote(NetworkManager client, NetworkAnimator onServer) =>
            ((NetworkObject)client.ClientManager.Spawned[onServer.NetworkObject.ObjectId]).GetComponent<NetworkAnimator>();

        private NetworkManager CreateManager(NetworkTransport transport, NetworkObject registered)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.ServerManager.FindSceneObjects = () => new List<NetworkObject>();
            manager.ClientManager.FindSceneObjects = () => new List<NetworkObject>();
            manager.Prefabs.Register(registered);
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
