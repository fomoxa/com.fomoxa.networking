using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Networking.Simulation;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class NetworkPhysicsTest
    {
        private const double FrameSeconds = 1.0 / 60;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private InMemoryNetworkTransport network;
        private SimulationMode originalMode;

        [SetUp]
        public void CreateNetwork()
        {
            originalMode = UnityEngine.Physics.simulationMode;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (NetworkManager manager in managers)
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
                manager.ReleasePhysicsSimulation();
            }

            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            managers.Clear();
            created.Clear();
            Assert.AreEqual(0, PhysicsSimulationOwner.Owners);
            Assert.AreEqual(originalMode, UnityEngine.Physics.simulationMode);
        }

        [Test]
        public void AnEmptyPhysicsFieldAddsARigidbodyBackendThatDoesNotSimulate()
        {
            NetworkManager manager = CreateManager(null);

            var physics = manager.Physics as RigidbodyPhysics;
            Assert.IsNotNull(physics);
            Assert.AreSame(manager.gameObject, physics.gameObject);
            Assert.IsFalse(physics.Simulates);
            Assert.AreEqual(0, PhysicsSimulationOwner.Owners);
            Assert.AreEqual(PhysicsBackend.Rigidbody, manager.ServerManager.Objects.PhysicsBackend);
        }

        [Test]
        public void ASimulatingRigidbodyBackendTakesTheSimulationModeUntilReleased()
        {
            var owner = new GameObject("Physics");
            created.Add(owner);
            NetworkManager manager = CreateManager(TestPhysics.Rigidbody(owner, true));

            Assert.AreEqual(1, PhysicsSimulationOwner.Owners);
            Assert.AreEqual(SimulationMode.Script, UnityEngine.Physics.simulationMode);

            manager.ReleasePhysicsSimulation();

            Assert.AreEqual(0, PhysicsSimulationOwner.Owners);
            Assert.AreEqual(originalMode, UnityEngine.Physics.simulationMode);
        }

        [Test]
        public void AnotherBackendGivesItsCodeIsBegunSteppedAndReleased()
        {
            var owner = new GameObject("Physics");
            created.Add(owner);
            var physics = owner.AddComponent<RecordingPhysics>();
            NetworkManager manager = CreateManager(physics);

            Assert.AreEqual(PhysicsBackend.Rapier, manager.ServerManager.Objects.PhysicsBackend);
            Assert.AreEqual(PhysicsBackend.Rapier, manager.ClientManager.Objects.PhysicsBackend);
            CollectionAssert.AreEqual(new[] { "begin" }, physics.Calls);

            for (int frame = 1; frame <= 4; frame++)
            {
                manager.RunFrameStart(FrameSeconds, TimeSpan.FromSeconds(1 + frame * FrameSeconds));
                manager.RunFrameEnd();
            }

            manager.ReleasePhysicsSimulation();

            Assert.AreEqual("begin", physics.Calls[0]);
            Assert.Contains("step", physics.Calls);
            Assert.AreEqual("release", physics.Calls[physics.Calls.Count - 1]);
            Assert.AreEqual(0, PhysicsSimulationOwner.Owners);
        }

        [Test]
        public void ARigidbodyBackendSimulatesForOneManagerOnly()
        {
            var owner = new GameObject("Physics");
            created.Add(owner);
            RigidbodyPhysics physics = TestPhysics.Rigidbody(owner, true);
            CreateManager(physics);

            Assert.Throws<InvalidOperationException>(() => CreateManager(physics));
            Assert.AreEqual(1, PhysicsSimulationOwner.Owners);
        }

        private NetworkManager CreateManager(NetworkPhysics physics)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            created.Add(manager.gameObject);
            managers.Add(manager);
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = network;
            serialized.FindProperty("physics").objectReferenceValue = physics;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            return manager;
        }
    }
}
