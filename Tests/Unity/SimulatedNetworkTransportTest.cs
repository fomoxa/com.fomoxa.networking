using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Tests
{
    public sealed class SimulatedNetworkTransportTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();

        [TearDown]
        public void DestroyEverything()
        {
            SimulatedNetworkTransport.DebugBuildOverride = null;
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
        }

        private SimulatedNetworkTransport Simulated(NetworkTransport inner, Action<SerializedObject> configure = null)
        {
            var gameObject = new GameObject("Simulated");
            created.Add(gameObject);
            SimulatedNetworkTransport simulated = gameObject.AddComponent<SimulatedNetworkTransport>();
            var serialized = new SerializedObject(simulated);
            serialized.FindProperty("transport").objectReferenceValue = inner;
            configure?.Invoke(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return simulated;
        }

        private UdpNetworkTransport Udp()
        {
            var gameObject = new GameObject("Udp");
            created.Add(gameObject);
            return gameObject.AddComponent<UdpNetworkTransport>();
        }

        private static void BadNetwork(SerializedObject serialized)
        {
            serialized.FindProperty("latencyMilliseconds").floatValue = 50f;
            serialized.FindProperty("jitterMilliseconds").floatValue = 10f;
            serialized.FindProperty("packetLoss").floatValue = 2f;
            serialized.FindProperty("duplicate").floatValue = 1f;
            serialized.FindProperty("reorder").floatValue = 3f;
            serialized.FindProperty("maxHeldPackets").intValue = 256;
        }

        [Test]
        public void WithoutATransportToSimulateCreatingTheFactoryThrows()
        {
            SimulatedNetworkTransport simulated = Simulated(null);

            Assert.Throws<InvalidOperationException>(() => simulated.CreateFactory());
        }

        [Test]
        public void ATransportCannotSimulateItself()
        {
            SimulatedNetworkTransport simulated = Simulated(null);
            var serialized = new SerializedObject(simulated);
            serialized.FindProperty("transport").objectReferenceValue = simulated;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.Throws<InvalidOperationException>(() => simulated.CreateFactory());
        }

        [Test]
        public void InTheEditorTheFactorySimulatesTheAssignedTransportWithTheFields()
        {
            SimulatedNetworkTransport simulated = Simulated(Udp(), BadNetwork);

            ITransportFactory factory = simulated.CreateFactory();

            Assert.IsInstanceOf<SimulatedTransportFactory>(factory);
            Assert.AreEqual(new UdpTransportFactory().FrameBudget, factory.FrameBudget);
            Assert.AreEqual(TimeSpan.FromMilliseconds(50), simulated.Conditions.Latency);
            Assert.AreEqual(TimeSpan.FromMilliseconds(10), simulated.Conditions.Jitter);
            Assert.AreEqual(2, simulated.Conditions.PacketLoss, 1e-6);
            Assert.AreEqual(1, simulated.Conditions.Duplicate, 1e-6);
            Assert.AreEqual(3, simulated.Conditions.Reorder, 1e-6);
            Assert.AreEqual(256, simulated.Conditions.MaxHeldPackets);
        }

        [Test]
        public void ByDefaultTheFieldsChangeNothingAndHoldUpTo1024Packets()
        {
            SimulatedNetworkTransport simulated = Simulated(Udp());

            simulated.CreateFactory();

            Assert.AreEqual(TimeSpan.Zero, simulated.Conditions.Latency);
            Assert.AreEqual(TimeSpan.Zero, simulated.Conditions.Jitter);
            Assert.AreEqual(0, simulated.Conditions.PacketLoss);
            Assert.AreEqual(0, simulated.Conditions.Duplicate);
            Assert.AreEqual(0, simulated.Conditions.Reorder);
            Assert.AreEqual(1024, simulated.Conditions.MaxHeldPackets);
        }

        [Test]
        public void EditingAFieldAfterTheFactoryExistsChangesTheConditionsItUses()
        {
            SimulatedNetworkTransport simulated = Simulated(Udp());
            simulated.CreateFactory();

            var serialized = new SerializedObject(simulated);
            serialized.FindProperty("latencyMilliseconds").floatValue = 80f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(TimeSpan.FromMilliseconds(80), simulated.Conditions.Latency);
        }

        [Test]
        public void InAReleaseBuildTheRealFactoryIsUsedAndAWarningIsLogged()
        {
            SimulatedNetworkTransport.DebugBuildOverride = false;
            SimulatedNetworkTransport simulated = Simulated(Udp(), BadNetwork);
            LogAssert.Expect(LogType.Warning, new Regex("network simulation runs only in the Editor and in Development builds; UdpNetworkTransport is used directly"));

            ITransportFactory factory = simulated.CreateFactory();

            Assert.IsInstanceOf<UdpTransportFactory>(factory);
        }

        [Test]
        public void AHostOverASimulatedTransportConnectsItsClientThroughTheUnsimulatedLoopback()
        {
            SimulatedNetworkTransport simulated = Simulated(Udp(), serialized => serialized.FindProperty("latencyMilliseconds").floatValue = 200f);
            var gameObject = new GameObject("NetworkManager");
            created.Add(gameObject);
            NetworkManager host = gameObject.AddComponent<NetworkManager>();
            var serializedManager = new SerializedObject(host);
            serializedManager.FindProperty("transport").objectReferenceValue = simulated;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();
            host.Registry = TestObjects.Registry(new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }));
            host.Initialize();
            managers.Add(host);

            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            TimeSpan now = TimeSpan.Zero;
            for (int frame = 0; frame < 20; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                host.RunFrameStart(FrameSeconds, now);
                host.RunFrameEnd();
            }

            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
        }
    }
}
