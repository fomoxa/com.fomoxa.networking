using System;
using System.Collections.Generic;
using System.Linq;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class TrafficStatisticsTest
    {
        private const uint OrderId = 0x2000_0003;
        private const uint PositionId = 0x2000_0004;
        private const int UdpBudget = 1200;

        private static Schema HostSchema() =>
            new Schema(0xCAFE, new[]
            {
                new MessageSchema(OrderId, 0xF00D, new ulong[] { 0xF00D }),
                new MessageSchema(PositionId, 0xF00E, new ulong[] { 0xF00E }),
            });

        private static SessionProtocol Protocol(int frameBudget)
        {
            var channels = new MessageChannels();
            channels.Set(OrderId, Channel.ReliableOrdered);
            return TestBundles.Protocol(channels, frameBudget);
        }

        private sealed class DataFrameTransport : ITransport
        {
            private readonly ITransport inner;
            private readonly Func<int, int> copiesOfDataFrame;
            private int dataFrames;

            public DataFrameTransport(ITransport inner, Func<int, int> copiesOfDataFrame)
            {
                this.inner = inner;
                this.copiesOfDataFrame = copiesOfDataFrame;
            }

            public TransportKind Kind => inner.Kind;

            public SendOutcome Send(ReadOnlySpan<byte> bytes)
            {
                if (bytes[0] != (byte)FrameType.Data)
                {
                    return inner.Send(bytes);
                }

                SendOutcome outcome = SendOutcome.Ok;
                int copies = copiesOfDataFrame(dataFrames++);
                for (int copy = 0; copy < copies; copy++)
                {
                    outcome = inner.Send(bytes);
                }

                return outcome;
            }

            public ReceiveOutcome Receive(Span<byte> buffer) => inner.Receive(buffer);

            public void CloseGracefully() => inner.CloseGracefully();

            public void Dispose() => inner.Dispose();

            public void RestartCount() => dataFrames = 0;
        }

        private sealed class Host
        {
            public readonly LoopbackListener Listener = new LoopbackListener(256);
            public readonly List<byte> ServerOrders = new List<byte>();
            public readonly List<ulong> PeerIds = new List<ulong>();
            public readonly ServerSession Server;
            public readonly List<ClientSession> Clients = new List<ClientSession>();
            private readonly SessionLimits limits;
            private readonly int frameBudget;
            public TimeSpan Now = TimeSpan.Zero;

            public Host(bool collectStatistics = true, int frameBudget = FomoxaWire.MaxDataFrameSize)
            {
                this.frameBudget = frameBudget;
                limits = new SessionLimits { CollectStatistics = collectStatistics };
                var serverDispatcher = new MessageDispatcher(HostSchema());
                serverDispatcher.Register(OrderId, (peerId, payload) => ServerOrders.Add(payload.Span[0]));
                serverDispatcher.Register(PositionId, (peerId, payload) => { });
                Server = new ServerSession(HostSchema(), new SessionConfig(), limits, serverDispatcher, Protocol(frameBudget)).SendingLocalPeer();
                Server.OnRemoteConnectionState += args =>
                {
                    if (args.State == ConnectionState.Starting)
                    {
                        PeerIds.Add(args.PeerId);
                    }
                };
                Server.Start(Listener);
            }

            public ClientSession Client => Clients[0];

            public ClientSession Connect(Func<int, int> copiesOfDataFrame = null)
            {
                var clientDispatcher = new MessageDispatcher(HostSchema());
                clientDispatcher.Register(OrderId, (peerId, payload) => { });
                var client = new ClientSession(HostSchema(), new SessionConfig(), limits, clientDispatcher, Protocol(frameBudget)).AcceptingLocalPeer();
                var transport = new DataFrameTransport(Listener.Connect(), copiesOfDataFrame ?? (frame => 1));
                client.Start(transport, Now);
                Clients.Add(client);
                Step(20);
                transport.RestartCount();
                return client;
            }

            public void Step(int count)
            {
                for (int step = 0; step < count; step++)
                {
                    Now += TimeSpan.FromMilliseconds(16);
                    Server.Tick(Now);
                    foreach (ClientSession client in Clients)
                    {
                        client.Tick(Now);
                    }

                    Server.Flush();
                    foreach (ClientSession client in Clients)
                    {
                        client.Flush();
                    }
                }
            }
        }

        private static MessageTraffic Row(TrafficDirection direction, uint messageId)
        {
            Assert.IsTrue(direction.Messages.TryGetValue(messageId, out MessageTraffic row), $"no row for message {messageId:X8}");
            return row;
        }

        private static void AreEqual(long count, long bytes, TrafficCount actual)
        {
            Assert.AreEqual(count, actual.Count, "count");
            Assert.AreEqual(bytes, actual.Bytes, "bytes");
        }

        private static void AreEqual(TrafficCount expected, TrafficCount actual) => AreEqual(expected.Count, expected.Bytes, actual);

        private static byte[] Pattern(int length)
        {
            var data = new byte[length];
            for (int index = 0; index < length; index++)
            {
                data[index] = (byte)(index * 7 + 1);
            }

            return data;
        }

        [Test]
        public void SessionsHaveNoStatisticsUnlessTheLimitsCollectThem()
        {
            var host = new Host(collectStatistics: false);
            host.Connect();

            Assert.IsNull(host.Server.Statistics);
            Assert.IsNull(host.Client.Statistics);
            Assert.IsFalse(new SessionLimits().CollectStatistics);
        }

        [Test]
        public void NewMessagesAreCountedOnBothSidesWithTheLengthOfTheirEntry()
        {
            var host = new Host();
            host.Connect();

            host.Client.Send(PositionId, new byte[] { 1, 2, 3 });
            host.Client.Send(OrderId, new byte[] { 4 });
            host.Step(2);

            AreEqual(1, 3, Row(host.Client.Statistics.Sent, PositionId).New);
            AreEqual(1, SeqHeader.Length + 1, Row(host.Client.Statistics.Sent, OrderId).New);
            AreEqual(1, 3, Row(host.Server.Statistics.Received, PositionId).New);
            AreEqual(1, SeqHeader.Length + 1, Row(host.Server.Statistics.Received, OrderId).New);
        }

        [Test]
        public void EveryBundleSentOverALosslessLinkIsCountedOnceOnTheOtherSide()
        {
            var host = new Host();
            host.Connect();

            for (byte value = 1; value <= 5; value++)
            {
                host.Client.Send(OrderId, new[] { value });
                host.Server.Send(host.PeerIds[0], OrderId, new[] { value });
                host.Step(1);
            }

            host.Step(10);

            TrafficStatistics client = host.Client.Statistics;
            ServerTrafficStatistics server = host.Server.Statistics;
            Assert.Greater(client.Sent.Bundles.Count, 0);
            AreEqual(client.Sent.Bundles, server.Received.Bundles);
            AreEqual(server.Sent.Bundles, client.Received.Bundles);
            AreEqual(client.Sent.Total.New, server.Received.Total.New);
            AreEqual(client.Sent.Total.Ack, server.Received.Total.Ack);
            AreEqual(server.Sent.Total.New, client.Received.Total.New);
            AreEqual(server.Sent.Total.Ack, client.Received.Total.Ack);
        }

        [Test]
        public void TotalsAreTheSumOfTheRows()
        {
            var host = new Host();
            host.Connect();

            host.Client.Send(PositionId, new byte[] { 1, 2, 3 });
            host.Client.Send(OrderId, new byte[] { 4 });
            host.Step(10);

            TrafficDirection sent = host.Client.Statistics.Sent;
            Assert.AreEqual(sent.Messages.Values.Sum(row => row.New.Count), sent.Total.New.Count);
            Assert.AreEqual(sent.Messages.Values.Sum(row => row.New.Bytes), sent.Total.New.Bytes);
            Assert.AreEqual(sent.Messages.Values.Sum(row => row.Ack.Count), sent.Total.Ack.Count);
            Assert.AreEqual(sent.Messages.Values.Sum(row => row.Ack.Bytes), sent.Total.Ack.Bytes);
        }

        [Test]
        public void AcksAreCountedUnderTheAckModelOnly()
        {
            var host = new Host();
            host.Connect();

            host.Client.Send(OrderId, new byte[] { 4 });
            host.Step(10);

            MessageTraffic sentAcks = Row(host.Server.Statistics.Sent, TestBundles.AckId);
            Assert.Greater(sentAcks.Ack.Count, 0);
            Assert.Greater(sentAcks.Ack.Bytes, 0);
            AreEqual(0, 0, sentAcks.New);
            AreEqual(sentAcks.Ack, Row(host.Client.Statistics.Received, TestBundles.AckId).Ack);
            AreEqual(0, 0, Row(host.Client.Statistics.Sent, OrderId).Ack);
        }

        [Test]
        public void ALostReliableMessageIsCountedAsRepeatedByTheSenderAndAsNewByTheReceiver()
        {
            var host = new Host();
            host.Connect(frame => frame == 0 ? 0 : 1);

            host.Client.Send(OrderId, new byte[] { 4 });
            host.Step(60);

            MessageTraffic sent = Row(host.Client.Statistics.Sent, OrderId);
            MessageTraffic received = Row(host.Server.Statistics.Received, OrderId);
            Assert.AreEqual(new byte[] { 4 }, host.ServerOrders.ToArray());
            AreEqual(1, SeqHeader.Length + 1, sent.New);
            Assert.GreaterOrEqual(sent.Repeated.Count, 1);
            Assert.AreEqual(sent.Repeated.Count * (SeqHeader.Length + 1), sent.Repeated.Bytes);
            AreEqual(1, SeqHeader.Length + 1, received.New);
            AreEqual(0, 0, received.Repeated);
        }

        [Test]
        public void ADuplicatedReliableEntryIsCountedAsRepeatedByTheReceiver()
        {
            var host = new Host();
            host.Connect(frame => frame == 0 ? 2 : 1);

            host.Client.Send(OrderId, new byte[] { 4 });
            host.Step(10);

            MessageTraffic received = Row(host.Server.Statistics.Received, OrderId);
            Assert.AreEqual(new byte[] { 4 }, host.ServerOrders.ToArray());
            AreEqual(1, SeqHeader.Length + 1, received.New);
            AreEqual(1, SeqHeader.Length + 1, received.Repeated);
            AreEqual(0, 0, Row(host.Client.Statistics.Sent, OrderId).Repeated);
        }

        [Test]
        public void ASplitMessageCountsItsFragmentsUnderTheFragmentModelsAndItselfOnceAsSplit()
        {
            var host = new Host(frameBudget: UdpBudget);
            host.Connect();
            byte[] large = Pattern(3000);

            host.Client.Send(OrderId, large);
            host.Step(10);

            TrafficDirection sent = host.Client.Statistics.Sent;
            TrafficDirection received = host.Server.Statistics.Received;
            MessageTraffic sentStart = Row(sent, TestBundles.FragmentStartId);
            MessageTraffic sentParts = Row(sent, TestBundles.FragmentPartId);
            Assert.AreEqual(1, sentStart.Fragment.Count);
            Assert.Greater(sentParts.Fragment.Count, 0);
            AreEqual(0, 0, sentStart.New);
            AreEqual(1, large.Length, Row(sent, OrderId).Split);
            AreEqual(0, 0, Row(sent, OrderId).New);
            AreEqual(sentStart.Fragment, Row(received, TestBundles.FragmentStartId).Fragment);
            AreEqual(sentParts.Fragment, Row(received, TestBundles.FragmentPartId).Fragment);
            AreEqual(1, large.Length, Row(received, OrderId).Split);
            AreEqual(0, 0, Row(received, OrderId).New);
            Assert.AreEqual(sent.Total.Fragment.Count, sentStart.Fragment.Count + sentParts.Fragment.Count);
            Assert.AreEqual(1, sent.Total.Split.Count);
        }

        [Test]
        public void EachPeerHasItsOwnStatisticsWhileConnectedAndTheTotalKeepsThemAfterward()
        {
            var host = new Host();
            ClientSession first = host.Connect();
            ClientSession second = host.Connect();
            ulong firstId = host.PeerIds[0];
            ulong secondId = host.PeerIds[1];

            first.Send(PositionId, new byte[] { 1 });
            second.Send(PositionId, new byte[] { 2, 3 });
            second.Send(PositionId, new byte[] { 4, 5 });
            host.Step(2);

            ServerTrafficStatistics server = host.Server.Statistics;
            AreEqual(1, 1, Row(server.Of(firstId).Received, PositionId).New);
            AreEqual(2, 4, Row(server.Of(secondId).Received, PositionId).New);
            AreEqual(3, 5, Row(server.Received, PositionId).New);
            Assert.AreEqual(
                server.Of(firstId).Received.Bundles.Count + server.Of(secondId).Received.Bundles.Count,
                server.Received.Bundles.Count);

            host.Server.Disconnect(firstId);
            host.Step(5);

            Assert.IsNull(server.Of(firstId));
            Assert.IsNotNull(server.Of(secondId));
            AreEqual(3, 5, Row(server.Received, PositionId).New);
            Assert.IsNull(server.Of(12345));
        }

        [Test]
        public void TheTotalsSurviveAServerRestartAndAClientReconnect()
        {
            var host = new Host();
            ClientSession client = host.Connect();
            client.Send(PositionId, new byte[] { 1 });
            host.Step(2);
            TrafficStatistics clientStatistics = client.Statistics;
            ServerTrafficStatistics serverStatistics = host.Server.Statistics;

            client.Stop();
            host.Server.Stop();
            host.Server.Start(host.Listener);
            client.Start(host.Listener.Connect(), host.Now);
            host.Step(20);
            client.Send(PositionId, new byte[] { 2 });
            host.Step(2);

            Assert.AreSame(clientStatistics, client.Statistics);
            Assert.AreSame(serverStatistics, host.Server.Statistics);
            AreEqual(2, 2, Row(client.Statistics.Sent, PositionId).New);
            AreEqual(2, 2, Row(host.Server.Statistics.Received, PositionId).New);
        }

        [Test]
        public void TheRegistryNamesMessagesThatWereNamedAndNoOthers()
        {
            var registry = new FomoxaRegistry();
            registry.SetName(OrderId, "Order.net");

            Assert.AreEqual("Order.net", registry.NameOf(OrderId));
            Assert.IsNull(registry.NameOf(PositionId));
            Assert.Throws<ArgumentNullException>(() => registry.SetName(PositionId, null));
        }

        [Test]
        public void NetworkSettingsCollectStatisticsOnlyWhenAsked()
        {
            Assert.IsFalse(new NetworkSettings().CollectStatistics);

            NetworkRuntime quiet = Runtime(new NetworkSettings());
            NetworkRuntime counting = Runtime(new NetworkSettings { CollectStatistics = true });

            Assert.IsNull(quiet.ServerManager.Statistics);
            Assert.IsNull(quiet.ClientManager.Statistics);
            Assert.IsNotNull(counting.ServerManager.Statistics);
            Assert.IsNotNull(counting.ClientManager.Statistics);
        }

        private static NetworkRuntime Runtime(NetworkSettings settings)
        {
            var host = new ServerSceneContentTest.FakeHost();
            var backends = new NetworkBackends(
                new ServerSceneContentTest.SceneBackend(host),
                host,
                new ClientEntitiesTest.FakeClientBackend(new List<string>()),
                new ClientManagerTest.NoScenes(),
                new ClientPredictionTest.FakeBackend());
            return new NetworkRuntime(
                TestObjects.Registry(),
                settings,
                new ClientManagerTest.Factory(),
                backends,
                () => TimeSpan.Zero,
                new NetworkLog(exception => throw exception, message => { }));
        }
    }
}
