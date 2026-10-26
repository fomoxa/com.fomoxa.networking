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

        [Test]
        public void ATlsRejectionStopsTheClientWithoutRetryAndLogsTheReason()
        {
            var rig = new Rig();
            rig.Factory.Next = () => new RejectingConnector("the TLS server game.example was rejected: pin mismatch");
            var stops = new List<ConnectionStateArgs>();
            rig.Client.OnClientConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    stops.Add(args);
                }
            };

            rig.Client.StartConnection("game.example", 443);
            rig.Run(100);

            Assert.AreEqual(1, stops.Count);
            Assert.AreEqual(StopReason.TlsRejected, stops[0].Reason);
            Assert.IsFalse(stops[0].WillRetry);
            Assert.AreEqual(1, rig.Factory.Connectors.Count);
            CollectionAssert.AreEqual(new[] { "Fomoxa stopped the client: the TLS server game.example was rejected: pin mismatch" }, rig.Errors);
        }

        [Test]
        public void AConnectorWithoutAReasonStillStopsWithTlsRejected()
        {
            var rig = new Rig();
            rig.Factory.Next = () => new RejectingConnector(null);
            var stops = new List<StopReason>();
            rig.Client.OnClientConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    stops.Add(args.Reason);
                }
            };

            rig.Client.StartConnection("game.example", 443);
            rig.Run(3);

            CollectionAssert.AreEqual(new[] { StopReason.TlsRejected }, stops);
            CollectionAssert.AreEqual(new[] { "Fomoxa stopped the client: the TLS server was rejected" }, rig.Errors);
        }

        private sealed class RejectingConnector : ITransportConnector, ITlsRejection
        {
            public RejectingConnector(string rejection)
            {
                TlsRejection = rejection;
            }

            public string TlsRejection { get; }

            public ConnectStatus Poll(out ITransport transport)
            {
                transport = null;
                return ConnectStatus.TlsRejected;
            }

            public void Dispose()
            {
            }
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
                var log = new NetworkLog(exception => throw exception, message => { }, Errors.Add);
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

            public List<string> Errors { get; } = new List<string>();

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

            public Func<ITransportConnector> Next { get; set; }

            public int FrameBudget => FomoxaWire.MaxDataFrameSize;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                boundPort = 7777;
                return new LoopbackListener(8);
            }

            public ITransportConnector CreateConnector(string address, ushort port)
            {
                Connectors.Add($"{address}:{port}");
                return Next?.Invoke() ?? new PendingConnector();
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
