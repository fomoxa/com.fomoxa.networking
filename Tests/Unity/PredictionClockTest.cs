using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using Fomoxa.Networking.Timing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class PredictionClockTest
    {
        private const double FrameSeconds = 1.0 / 60;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private InMemoryNetworkTransport network;
        private TimeSpan now;

        [SetUp]
        public void CreateNetwork()
        {
            now = TimeSpan.FromSeconds(1);
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
            }

            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            managers.Clear();
            created.Clear();
        }

        [Test]
        public void ARemoteClientFollowsTheServerTickRateAndSyncsItsClock()
        {
            NetworkManager server = CreateManager(60, TimingMode.Tick);
            server.ServerManager.StartConnection(1);
            NetworkManager client = CreateManager(30, TimingMode.Tick);
            ulong peerId = 0;
            server.ServerManager.OnRemoteConnectionState += args => peerId = args.PeerId;
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(60);

            TimeManager time = client.TimeManager;
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            Assert.AreEqual(60, time.TickRate);
            Assert.IsTrue(time.ClockSynced);
            Assert.AreEqual(server.TimeManager.Tick, time.ServerTick, 2.0);
            Assert.Greater(time.PredictionTick, time.ServerTick);
            Assert.Greater(client.ClientManager.Rtt, TimeSpan.Zero);
            Assert.IsTrue(server.ServerManager.HasRtt(peerId));
            Assert.Greater(server.ServerManager.RttOf(peerId), TimeSpan.Zero);

            client.ClientManager.StopConnection();

            Assert.AreEqual(30, time.TickRate);
            Assert.IsFalse(time.ClockSynced);
            Assert.AreEqual(0u, time.PredictionTick);
        }

        [Test]
        public void AServerAndAHostTickInTheirOwnTime()
        {
            NetworkManager host = CreateManager(60, TimingMode.Tick);
            host.ServerManager.StartConnection(1);
            RunFrames(5);

            Assert.IsTrue(host.TimeManager.ClockSynced);
            Assert.AreEqual(host.TimeManager.Tick, host.TimeManager.ServerTick);
            Assert.AreEqual(host.TimeManager.Tick, host.TimeManager.PredictionTick);

            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);

            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
            Assert.IsTrue(host.TimeManager.ClockSynced);
            Assert.AreEqual(host.TimeManager.Tick, host.TimeManager.PredictionTick);
            Assert.AreEqual(TimeSpan.Zero, host.ClientManager.Rtt);
            Assert.AreEqual(60, host.TimeManager.TickRate);
        }

        [Test]
        public void AVariableModeClientPredictsTheServerTick()
        {
            NetworkManager server = CreateManager(60, TimingMode.Tick);
            server.ServerManager.StartConnection(1);
            NetworkManager client = CreateManager(60, TimingMode.Variable);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(60);

            Assert.IsTrue(client.TimeManager.ClockSynced);
            Assert.AreEqual(client.TimeManager.ServerTick, client.TimeManager.PredictionTick);
        }

        [Test]
        public void ATickScaleAboveOneStretchesTheTicksOfAFollowingClient()
        {
            var estimator = new ClockEstimator(new ClockSettings());
            estimator.SetTickRate(30);
            TimeSpan sampleTime = TimeSpan.FromMilliseconds(1100);
            estimator.AddSample(900, 500, ClockProtocol.NoInputLead, sampleTime);
            estimator.Advance(sampleTime);
            estimator.Advance(sampleTime);
            estimator.Advance(sampleTime);
            var following = new TimeManager(30, 3, TimingMode.Tick) { Clock = estimator, FollowsClock = () => true };
            var plain = new TimeManager(30, 3, TimingMode.Tick);

            Assert.AreEqual(1.10, estimator.TickScale, 1e-3);
            Assert.AreEqual(2, following.Advance(0.1));
            Assert.AreEqual(3, plain.Advance(0.1));
        }

        [Test]
        public void TheTickRateIsBetweenOneAndTheLargestUnsignedShort()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimeManager(0, 3, TimingMode.Tick));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimeManager(ushort.MaxValue + 1, 3, TimingMode.Tick));
            Assert.AreEqual(ushort.MaxValue, new TimeManager(ushort.MaxValue, 3, TimingMode.Tick).TickRate);
        }

        private NetworkManager CreateManager(int tickRate, TimingMode mode)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = network;
            serialized.FindProperty("tickRate").intValue = tickRate;
            serialized.FindProperty("timingMode").enumValueIndex = (int)mode;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.ServerManager.FindSceneObjects = () => new List<NetworkObject>();
            manager.ClientManager.FindSceneObjects = () => new List<NetworkObject>();
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
