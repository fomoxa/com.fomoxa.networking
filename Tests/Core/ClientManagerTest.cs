using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ClientManagerTest
    {
        [Test]
        public void AClientOfARunningServerConnectsThroughItsLocalLoopback()
        {
            var rig = new Rig();
            rig.Server.StartConnection(0);

            rig.Client.StartConnection("unused.invalid", 1);
            rig.Run(10);

            Assert.IsTrue(rig.Client.ConnectedLocally);
            Assert.AreEqual(ConnectionState.Started, rig.Client.State);
            Assert.AreEqual(1, rig.Server.PeerCount);
            Assert.AreEqual(0, rig.Factory.Connectors.Count);
            Assert.AreEqual(1, rig.ClockReads);
        }

        [Test]
        public void AClientWithoutARunningServerAsksTheTransportForAConnector()
        {
            var rig = new Rig();

            rig.Client.StartConnection("game.example", 7000);

            Assert.IsFalse(rig.Client.ConnectedLocally);
            CollectionAssert.AreEqual(new[] { "game.example:7000" }, rig.Factory.Connectors);
            Assert.AreEqual(1, rig.ClockReads);
            Assert.Throws<InvalidOperationException>(() => rig.Client.StartConnection("game.example", 7000));
        }

        private sealed class Rig
        {
            public readonly Factory Factory = new Factory();
            public readonly ServerSceneContentTest.FakeHost ServerHost = new ServerSceneContentTest.FakeHost();
            public readonly ServerManager Server;
            public readonly ClientManager Client;
            private TimeSpan now;

            public Rig()
            {
                MessageChannels channels = TestObjects.Channels();
                var log = new NetworkLog(exception => throw exception, message => { });
                Server = new ServerManager(
                    TestObjects.Schema(),
                    new SessionLimits(),
                    new SessionConfig(),
                    TestBundles.Protocol(channels),
                    TestObjects.Protocol(channels),
                    TestObjects.StateProtocol(channels),
                    TestObjects.TransformProtocol(channels),
                    TestObjects.SceneProtocol(channels),
                    TestObjects.ClockProtocol(),
                    TestObjects.InputProtocol(),
                    new RpcMessageIds(),
                    Factory,
                    8,
                    new ServerSceneContentTest.SceneBackend(ServerHost),
                    ServerHost,
                    log);
                Client = new ClientManager(
                    TestObjects.Schema(),
                    new SessionLimits(),
                    new SessionConfig(),
                    TestBundles.Protocol(channels),
                    TestObjects.Protocol(channels),
                    TestObjects.StateProtocol(channels),
                    TestObjects.TransformProtocol(channels),
                    TestObjects.SceneProtocol(channels),
                    TestObjects.ClockProtocol(),
                    new ClockSettings(),
                    TestObjects.InputProtocol(),
                    new TimeManager(30, 3, TimingMode.Tick),
                    new RpcMessageIds(),
                    new ReconnectPolicy(),
                    Factory,
                    Server,
                    new ClientEntitiesTest.FakeClientBackend(new List<string>()),
                    new NoScenes(),
                    new ClientPredictionTest.FakeBackend(),
                    () =>
                    {
                        ClockReads++;
                        return now;
                    },
                    log);
            }

            public int ClockReads { get; private set; }

            public void Run(int steps)
            {
                for (int step = 0; step < steps; step++)
                {
                    now += TimeSpan.FromMilliseconds(16);
                    Server.Tick(now);
                    Client.Tick(now);
                    Server.Flush();
                    Client.Flush();
                }
            }
        }

        internal sealed class Factory : ITransportFactory
        {
            public List<string> Connectors { get; } = new List<string>();

            public int FrameBudget => FomoxaWire.MaxDataFrameSize;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                boundPort = 7777;
                return new LoopbackListener(8);
            }

            public ITransportConnector CreateConnector(string address, ushort port)
            {
                Connectors.Add($"{address}:{port}");
                return new PendingConnector();
            }
        }

        internal sealed class PendingConnector : ITransportConnector
        {
            public ConnectStatus Poll(out ITransport transport)
            {
                transport = null;
                return ConnectStatus.Pending;
            }

            public void Dispose()
            {
            }
        }

        internal sealed class NoScenes : ISceneHost
        {
            public bool TryLoad(uint sceneId, Action loaded, Action failed) => false;

            public void Unload(uint sceneId, Action unloaded) => unloaded();
        }
    }
}
