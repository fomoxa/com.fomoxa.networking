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
    public sealed class SimulatedTransportTest
    {
        private const uint OrderId = 0x2000_0003;

        private TimeSpan now;

        private sealed class RecordingTransport : ITransport
        {
            public RecordingTransport(TransportKind kind)
            {
                Kind = kind;
            }

            public TransportKind Kind { get; }

            public List<byte[]> Sent { get; } = new List<byte[]>();

            public Queue<SendOutcome> SendOutcomes { get; } = new Queue<SendOutcome>();

            public Queue<byte[]> Incoming { get; } = new Queue<byte[]>();

            public ReceiveOutcome EndOfIncoming { get; set; } = ReceiveOutcome.WouldBlock;

            public bool ClosedGracefully { get; private set; }

            public SendOutcome Send(ReadOnlySpan<byte> bytes)
            {
                SendOutcome outcome = SendOutcomes.Count > 0 ? SendOutcomes.Dequeue() : SendOutcome.Ok;
                if (outcome.Signal == TransportSignal.Ok)
                {
                    Sent.Add(bytes.ToArray());
                }
                else if (outcome.Signal == TransportSignal.Partial)
                {
                    Sent.Add(bytes.Slice(0, outcome.Count).ToArray());
                }

                return outcome;
            }

            public ReceiveOutcome Receive(Span<byte> buffer)
            {
                if (Incoming.Count == 0)
                {
                    return EndOfIncoming;
                }

                byte[] next = Incoming.Peek();
                if (next.Length > buffer.Length)
                {
                    return ReceiveOutcome.NeedCapacity(next.Length);
                }

                Incoming.Dequeue();
                next.CopyTo(buffer);
                return ReceiveOutcome.Ok(next.Length);
            }

            public void CloseGracefully() => ClosedGracefully = true;

            public void Dispose()
            {
            }
        }

        private sealed class OneTransportFactory : ITransportFactory
        {
            public OneTransportFactory(int frameBudget)
            {
                FrameBudget = frameBudget;
            }

            public int FrameBudget { get; }

            public LoopbackListener Listener { get; } = new LoopbackListener(64);

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                boundPort = port;
                return Listener;
            }

            public ITransportConnector CreateConnector(string address, ushort port) => new ConnectedConnector(Listener.Connect());
        }

        private sealed class ConnectedConnector : ITransportConnector, ITlsRejection
        {
            private readonly ITransport transport;

            public ConnectedConnector(ITransport transport)
            {
                this.transport = transport;
            }

            public string TlsRejection => "rejected for the test";

            public ConnectStatus Poll(out ITransport connected)
            {
                connected = transport;
                return ConnectStatus.Connected;
            }

            public void Dispose()
            {
            }
        }

        [SetUp]
        public void ResetClock()
        {
            now = TimeSpan.FromSeconds(1);
        }

        private ITransport Simulate(ITransport inner, NetworkConditions conditions, int seed = 7) =>
            new SimulatedTransportFactory(new OneTransportFactory(1200), conditions, () => now, seed).Wrap(inner);

        private void Advance(double milliseconds) => now += TimeSpan.FromMilliseconds(milliseconds);

        private static byte[] Packet(int value) => new[] { (byte)value, (byte)(value >> 8) };

        private static int ValueOf(byte[] packet) => packet[0] | (packet[1] << 8);

        private static void Tick(ITransport transport) => transport.Receive(new byte[64]);

        [Test]
        public void WithNoConditionsEveryCallGoesStraightToTheRealTransport()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions());
            inner.SendOutcomes.Enqueue(SendOutcome.TooLarge);
            inner.SendOutcomes.Enqueue(SendOutcome.WouldBlock);
            inner.Incoming.Enqueue(Packet(9));

            Assert.AreEqual(TransportSignal.TooLarge, simulated.Send(Packet(1)).Signal);
            Assert.AreEqual(TransportSignal.WouldBlock, simulated.Send(Packet(2)).Signal);
            Assert.AreEqual(TransportSignal.Ok, simulated.Send(Packet(3)).Signal);
            var buffer = new byte[64];
            ReceiveOutcome received = simulated.Receive(buffer);

            Assert.AreEqual(new[] { 3 }, inner.Sent.Select(ValueOf).ToArray());
            Assert.AreEqual(TransportSignal.Ok, received.Signal);
            Assert.AreEqual(9, ValueOf(buffer));
            Assert.AreEqual(TransportKind.Message, simulated.Kind);
        }

        [Test]
        public void LatencyHoldsAPacketUntilItsTimeAndASendOrReceiveReleasesIt()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions { Latency = TimeSpan.FromMilliseconds(50) });

            Assert.AreEqual(TransportSignal.Ok, simulated.Send(Packet(1)).Signal);
            Advance(49);
            Tick(simulated);
            Assert.IsEmpty(inner.Sent);

            Advance(1);
            Tick(simulated);
            Assert.AreEqual(new[] { 1 }, inner.Sent.Select(ValueOf).ToArray());
        }

        [Test]
        public void JitterSpreadsEachDelayEvenlyAroundTheLatency()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions { Latency = TimeSpan.FromMilliseconds(50), Jitter = TimeSpan.FromMilliseconds(10) });
            var sentAt = new Dictionary<int, TimeSpan>();
            var delays = new List<double>();

            for (int step = 0; step < 4000; step++)
            {
                if (step < 2000)
                {
                    simulated.Send(Packet(step));
                    sentAt[step] = now;
                }

                int before = inner.Sent.Count;
                Advance(1);
                Tick(simulated);
                for (int index = before; index < inner.Sent.Count; index++)
                {
                    delays.Add((now - sentAt[ValueOf(inner.Sent[index])]).TotalMilliseconds);
                }
            }

            Assert.AreEqual(2000, delays.Count);
            Assert.GreaterOrEqual(delays.Min(), 40);
            Assert.LessOrEqual(delays.Max(), 61);
            Assert.AreEqual(50.5, delays.Average(), 1.0);
        }

        [Test]
        public void PacketLossDropsAboutThatShareOfPackets()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions { PacketLoss = 25 });

            for (int value = 0; value < 4000; value++)
            {
                Assert.AreEqual(TransportSignal.Ok, simulated.Send(Packet(value)).Signal);
            }

            Assert.AreEqual(3000, inner.Sent.Count, 120);
        }

        [Test]
        public void TheSameSeedLosesTheSamePackets()
        {
            var first = new RecordingTransport(TransportKind.Message);
            var second = new RecordingTransport(TransportKind.Message);
            ITransport one = Simulate(first, new NetworkConditions { PacketLoss = 50 }, seed: 42);
            ITransport two = Simulate(second, new NetworkConditions { PacketLoss = 50 }, seed: 42);

            for (int value = 0; value < 200; value++)
            {
                one.Send(Packet(value));
                two.Send(Packet(value));
            }

            Assert.AreEqual(first.Sent.Select(ValueOf).ToArray(), second.Sent.Select(ValueOf).ToArray());
            Assert.Less(first.Sent.Count, 200);
        }

        [Test]
        public void DuplicateSendsASecondCopy()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions { Duplicate = 100 });

            simulated.Send(Packet(1));
            simulated.Send(Packet(2));

            Assert.AreEqual(new[] { 1, 1, 2, 2 }, inner.Sent.Select(ValueOf).ToArray());
        }

        [Test]
        public void AReorderedPacketGoesOutRightAfterTheNextOneAndEditsApplyToTheNextPacket()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            var conditions = new NetworkConditions { Latency = TimeSpan.FromMilliseconds(10), Reorder = 100 };
            ITransport simulated = Simulate(inner, conditions);

            simulated.Send(Packet(1));
            conditions.Reorder = 0;
            simulated.Send(Packet(2));
            Advance(10);
            Tick(simulated);

            Assert.AreEqual(new[] { 2, 1 }, inner.Sent.Select(ValueOf).ToArray());
        }

        [Test]
        public void AReorderedPacketWithoutASuccessorLeavesAfterTwiceItsDelay()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions { Latency = TimeSpan.FromMilliseconds(10), Reorder = 100 });

            simulated.Send(Packet(1));
            Advance(19);
            Tick(simulated);
            Assert.IsEmpty(inner.Sent);

            Advance(1);
            Tick(simulated);
            Assert.AreEqual(new[] { 1 }, inner.Sent.Select(ValueOf).ToArray());
        }

        [Test]
        public void AFullQueueOfPacketsDropsTheOldest()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions { Latency = TimeSpan.FromMilliseconds(10), MaxHeldPackets = 3 });

            for (int value = 1; value <= 5; value++)
            {
                Assert.AreEqual(TransportSignal.Ok, simulated.Send(Packet(value)).Signal);
            }

            Advance(10);
            Tick(simulated);

            Assert.AreEqual(new[] { 3, 4, 5 }, inner.Sent.Select(ValueOf).ToArray());
        }

        [Test]
        public void AWouldBlockFromTheRealTransportKeepsThePacketAtTheHead()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions { Latency = TimeSpan.FromMilliseconds(10) });
            simulated.Send(Packet(1));
            simulated.Send(Packet(2));
            inner.SendOutcomes.Enqueue(SendOutcome.WouldBlock);

            Advance(10);
            Tick(simulated);
            Assert.IsEmpty(inner.Sent);

            Tick(simulated);
            Assert.AreEqual(new[] { 1, 2 }, inner.Sent.Select(ValueOf).ToArray());
        }

        [Test]
        public void AByteStreamKeepsEveryByteInOrderAndOnlyGetsLatencyAndJitter()
        {
            var inner = new RecordingTransport(TransportKind.Stream);
            ITransport simulated = Simulate(inner, new NetworkConditions
            {
                Latency = TimeSpan.FromMilliseconds(20),
                Jitter = TimeSpan.FromMilliseconds(15),
                PacketLoss = 50,
                Duplicate = 50,
                Reorder = 50,
            });
            var expected = new List<byte>();

            for (int value = 0; value < 300; value++)
            {
                Assert.AreEqual(TransportSignal.Ok, simulated.Send(Packet(value)).Signal);
                expected.AddRange(Packet(value));
                Advance(1);
                Tick(simulated);
            }

            Advance(40);
            Tick(simulated);

            Assert.AreEqual(expected.ToArray(), inner.Sent.SelectMany(chunk => chunk).ToArray());
        }

        [Test]
        public void APartialSendOnAByteStreamKeepsTheRestAtTheHead()
        {
            var inner = new RecordingTransport(TransportKind.Stream);
            ITransport simulated = Simulate(inner, new NetworkConditions { Latency = TimeSpan.FromMilliseconds(10) });
            simulated.Send(new byte[] { 1, 2, 3, 4 });
            simulated.Send(new byte[] { 5 });
            inner.SendOutcomes.Enqueue(SendOutcome.Partial(1));

            Advance(10);
            Tick(simulated);
            Assert.AreEqual(1, inner.Sent.Count);

            Tick(simulated);
            Assert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, inner.Sent.SelectMany(chunk => chunk).ToArray());
        }

        [Test]
        public void AFullByteStreamQueueAnswersWouldBlock()
        {
            var inner = new RecordingTransport(TransportKind.Stream);
            ITransport simulated = Simulate(inner, new NetworkConditions { Latency = TimeSpan.FromMilliseconds(10), MaxHeldPackets = 2 });

            Assert.AreEqual(TransportSignal.Ok, simulated.Send(Packet(1)).Signal);
            Assert.AreEqual(TransportSignal.Ok, simulated.Send(Packet(2)).Signal);
            Assert.AreEqual(TransportSignal.WouldBlock, simulated.Send(Packet(3)).Signal);
        }

        [Test]
        public void ReceivedPacketsAreDelayedTooAndAskForCapacityWhenTheBufferIsSmall()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions { Latency = TimeSpan.FromMilliseconds(30) });
            inner.Incoming.Enqueue(new byte[] { 1, 2, 3, 4 });

            Assert.AreEqual(TransportSignal.WouldBlock, simulated.Receive(new byte[64]).Signal);
            Advance(30);
            ReceiveOutcome small = simulated.Receive(new byte[2]);
            var buffer = new byte[64];
            ReceiveOutcome received = simulated.Receive(buffer);

            Assert.AreEqual(TransportSignal.NeedCapacity, small.Signal);
            Assert.AreEqual(4, small.Count);
            Assert.AreEqual(TransportSignal.Ok, received.Signal);
            Assert.AreEqual(new byte[] { 1, 2, 3, 4 }, buffer.Take(received.Count).ToArray());
        }

        [Test]
        public void AClosedRealTransportIsReportedAfterTheHeldPacketsAreDelivered()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions { Latency = TimeSpan.FromMilliseconds(30) });
            inner.Incoming.Enqueue(Packet(5));
            inner.EndOfIncoming = ReceiveOutcome.Closed;

            Assert.AreEqual(TransportSignal.WouldBlock, simulated.Receive(new byte[64]).Signal);
            Advance(30);
            Assert.AreEqual(TransportSignal.Ok, simulated.Receive(new byte[64]).Signal);
            Assert.AreEqual(TransportSignal.Closed, simulated.Receive(new byte[64]).Signal);
        }

        [Test]
        public void ClosingGracefullySendsWhatIsHeldFirst()
        {
            var inner = new RecordingTransport(TransportKind.Message);
            ITransport simulated = Simulate(inner, new NetworkConditions { Latency = TimeSpan.FromMilliseconds(30) });
            simulated.Send(Packet(1));

            simulated.CloseGracefully();

            Assert.AreEqual(new[] { 1 }, inner.Sent.Select(ValueOf).ToArray());
            Assert.IsTrue(inner.ClosedGracefully);
        }

        [Test]
        public void TheFactoryWrapsAcceptedAndConnectedTransportsAndForwardsTheRest()
        {
            var real = new OneTransportFactory(1200);
            var factory = new SimulatedTransportFactory(real, new NetworkConditions(), () => now);

            IListenerTransport listener = factory.CreateListener(7000, out ushort bound);
            ITransportConnector connector = factory.CreateConnector("unused", 1);
            Assert.AreEqual(ConnectStatus.Connected, connector.Poll(out ITransport connected));
            AcceptOutcome accepted = listener.Accept();

            Assert.AreEqual(7000, bound);
            Assert.AreEqual(1200, factory.FrameBudget);
            Assert.IsInstanceOf<SimulatedTransport>(connected);
            Assert.AreEqual(AcceptStatus.Accepted, accepted.Status);
            Assert.IsInstanceOf<SimulatedTransport>(accepted.Transport);
            Assert.AreEqual("rejected for the test", ((ITlsRejection)connector).TlsRejection);
            Assert.IsFalse(factory.RequiresConnection);
            Assert.IsFalse(factory.CanUseConnection(new byte[] { 1 }));
        }

        [Test]
        public void ACompositeListenerSeesTheFrameBudgetOfEachTransportBehindTheSimulation()
        {
            var udp = new OneTransportFactory(1200);
            var tcp = new OneTransportFactory(9000);
            var factory = new SimulatedTransportFactory(new MultiTransportFactory(new ITransportFactory[] { udp, tcp }, new ushort[] { 0, 0 }), new NetworkConditions(), () => now);
            var server = new CompositeListener(new[] { factory.CreateListener(0, out _), new LoopbackListener(4) }, new[] { factory.FrameBudget, factory.FrameBudget });
            tcp.Listener.Connect();

            AcceptOutcome accepted = AcceptNext(server);

            Assert.AreEqual(AcceptStatus.Accepted, accepted.Status);
            Assert.IsTrue(server.TryTakeAcceptedBudget(out int budget));
            Assert.AreEqual(9000, budget);
            Assert.IsTrue(server.TryTakeAcceptedListener(out int index));
            Assert.AreEqual(0, index);
        }

        private static AcceptOutcome AcceptNext(IListenerTransport listener)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                AcceptOutcome outcome = listener.Accept();
                if (outcome.Status == AcceptStatus.Accepted)
                {
                    return outcome;
                }
            }

            return AcceptOutcome.Pending;
        }

        [Test]
        public void ReliableMessagesArriveOnceAndInOrderOverALossyDelayedLink()
        {
            var schema = new Schema(0xCAFE, new[] { new MessageSchema(OrderId, 0xF00D, new ulong[] { 0xF00D }) });
            var channels = new MessageChannels();
            channels.Set(OrderId, Channel.ReliableOrdered);
            var received = new List<byte>();
            var serverDispatcher = new MessageDispatcher(schema);
            serverDispatcher.Register(OrderId, (peerId, payload) => received.Add(payload.Span[0]));
            var real = new OneTransportFactory(FomoxaWire.MaxDataFrameSize);
            var conditions = new NetworkConditions { Latency = TimeSpan.FromMilliseconds(30), Jitter = TimeSpan.FromMilliseconds(10), PacketLoss = 10, Duplicate = 5, Reorder = 5 };
            var factory = new SimulatedTransportFactory(real, conditions, () => now, seed: 3);
            var server = new ServerSession(schema, new SessionConfig(), new SessionLimits(), serverDispatcher, TestBundles.Protocol(channels)).SendingLocalPeer();
            var client = new ClientSession(schema, new SessionConfig(), new SessionLimits(), new MessageDispatcher(schema), TestBundles.Protocol(channels)).AcceptingLocalPeer();
            server.Start(factory.CreateListener(0, out _));
            client.Start(factory.CreateConnector("unused", 0));

            for (int step = 0; step < 400 && client.State != ConnectionState.Started; step++)
            {
                Step(server, client);
            }

            Assert.AreEqual(ConnectionState.Started, client.State);
            for (byte value = 1; value <= 20; value++)
            {
                Assert.AreEqual(SendResult.Queued, client.Send(OrderId, new[] { value }));
                Step(server, client);
            }

            for (int step = 0; step < 400; step++)
            {
                Step(server, client);
            }

            Assert.AreEqual(Enumerable.Range(1, 20).Select(value => (byte)value).ToArray(), received.ToArray());
        }

        private void Step(ServerSession server, ClientSession client)
        {
            Advance(16);
            server.Tick(now);
            client.Tick(now);
            server.Flush();
            client.Flush();
        }

        [Test]
        public void NetworkConditionsRejectValuesOutOfRange()
        {
            var conditions = new NetworkConditions();

            Assert.Throws<ArgumentOutOfRangeException>(() => conditions.Latency = TimeSpan.FromMilliseconds(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => conditions.Jitter = TimeSpan.FromMilliseconds(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => conditions.PacketLoss = 101);
            Assert.Throws<ArgumentOutOfRangeException>(() => conditions.Duplicate = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => conditions.Reorder = 100.5);
            Assert.Throws<ArgumentOutOfRangeException>(() => conditions.MaxHeldPackets = 0);
            Assert.AreEqual(1024, conditions.MaxHeldPackets);
            Assert.AreEqual(TimeSpan.Zero, conditions.Latency);
            Assert.AreEqual(0, conditions.PacketLoss);
        }
    }
}
