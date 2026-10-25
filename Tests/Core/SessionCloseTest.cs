using System;
using System.Collections.Generic;
using System.Linq;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class SessionCloseTest
    {
        private const uint OrderId = 0x2000_0003;
        private const byte Failing = 0xFF;

        private static Schema HostSchema() =>
            new Schema(0xCAFE, new[] { new MessageSchema(OrderId, 0xF00D, new ulong[] { 0xF00D }) });

        private static MessageChannels Channels()
        {
            var channels = new MessageChannels();
            channels.Set(OrderId, Channel.ReliableOrdered);
            return channels;
        }

        private static byte[] Close(byte reason) =>
            SessionCloseNetAdapter.Instance.Encode(new SessionClose { Reason = reason }).ToArray();

        private sealed class Host
        {
            public readonly LoopbackListener Listener = new LoopbackListener(256);
            public readonly List<byte> ServerOrders = new List<byte>();
            public readonly List<byte> ClientOrders = new List<byte>();
            public readonly List<ConnectionStateArgs> PeerStops = new List<ConnectionStateArgs>();
            public readonly List<ConnectionStateArgs> ClientStops = new List<ConnectionStateArgs>();
            public readonly List<ulong> StartedPeers = new List<ulong>();
            public readonly ServerSession Server;
            public TimeSpan Time = TimeSpan.Zero;

            public Host(SessionLimits limits = null)
            {
                var dispatcher = new MessageDispatcher(HostSchema());
                dispatcher.Register(OrderId, (peerId, payload) =>
                {
                    if (payload.Span[0] == Failing)
                    {
                        throw new InvalidOperationException("handler failed");
                    }

                    ServerOrders.Add(payload.Span[0]);
                });
                Server = new ServerSession(HostSchema(), new SessionConfig(), limits ?? new SessionLimits(), dispatcher, TestBundles.Protocol(Channels()));
                Server.OnRemoteConnectionState += args =>
                {
                    if (args.State == ConnectionState.Started)
                    {
                        StartedPeers.Add(args.PeerId);
                    }
                    else if (args.State == ConnectionState.Stopped)
                    {
                        PeerStops.Add(args);
                    }
                };
                Server.Start(Listener);
            }

            public ClientSession Client { get; private set; }

            public ClientSession Connect()
            {
                var dispatcher = new MessageDispatcher(HostSchema());
                dispatcher.Register(OrderId, (peerId, payload) => ClientOrders.Add(payload.Span[0]));
                Client = new ClientSession(HostSchema(), new SessionConfig(), new SessionLimits(), dispatcher, TestBundles.Protocol(Channels()));
                Client.OnClientConnectionState += args =>
                {
                    if (args.State == ConnectionState.Stopped)
                    {
                        ClientStops.Add(args);
                    }
                };
                Client.Start(Listener.Connect(), Time);
                Step(20);
                return Client;
            }

            public void Step(int count)
            {
                for (int step = 0; step < count; step++)
                {
                    Time += TimeSpan.FromMilliseconds(16);
                    Server.Tick(Time);
                    Client?.Tick(Time);
                    Server.Flush();
                    Client?.Flush();
                }
            }
        }

        [Test]
        public void AKickedClientEndsWithServerClosedCarryingKicked()
        {
            var host = new Host();
            host.Connect();
            ulong peerId = host.StartedPeers.Single();

            Assert.IsTrue(host.Server.Kick(peerId));
            host.Step(5);

            Assert.AreEqual(StopReason.ServerClosed, host.ClientStops.Single().Reason);
            Assert.AreEqual(SessionCloseReason.Kicked, host.ClientStops.Single().CloseReason);
            Assert.AreEqual(StopReason.LocalStop, host.PeerStops.Single().Reason);
            Assert.AreEqual(SessionCloseReason.Unspecified, host.PeerStops.Single().CloseReason);
        }

        [Test]
        public void MessagesQueuedBeforeAKickArriveBeforeTheClose()
        {
            var host = new Host();
            host.Connect();
            ulong peerId = host.StartedPeers.Single();

            Assert.AreEqual(SendResult.Queued, host.Server.Send(peerId, OrderId, new byte[] { 1 }));
            Assert.AreEqual(SendResult.Queued, host.Server.Send(peerId, OrderId, new byte[] { 2 }));
            host.Server.Kick(peerId);
            host.Step(5);

            CollectionAssert.AreEqual(new byte[] { 1, 2 }, host.ClientOrders);
            Assert.AreEqual(StopReason.ServerClosed, host.ClientStops.Single().Reason);
        }

        [Test]
        public void KickReturnsFalseForAnUnknownPeerAndForAPeerAlreadyClosing()
        {
            var host = new Host(new SessionLimits { ReliableWindow = 1 });
            host.Connect();
            ulong peerId = host.StartedPeers.Single();
            host.Server.Send(peerId, OrderId, new byte[] { 1 });
            host.Server.Flush();

            Assert.IsFalse(host.Server.Kick(peerId + 100));
            Assert.IsTrue(host.Server.Kick(peerId));
            host.Server.Flush();
            Assert.AreEqual(0, host.PeerStops.Count);
            Assert.IsFalse(host.Server.Kick(peerId));
        }

        [Test]
        public void KickingAStartingPeerClosesItAtOnceWithoutSessionClose()
        {
            var host = new Host();
            var silent = host.Listener.Connect();
            host.Server.Tick(TimeSpan.FromMilliseconds(16));
            ulong peerId = 1;
            Assert.AreEqual(ConnectionState.Starting, host.Server.PeerState(peerId));

            Assert.IsTrue(host.Server.Kick(peerId));

            Assert.AreEqual(StopReason.LocalStop, host.PeerStops.Single().Reason);
            Assert.AreEqual(0, host.Server.PeerCount);
            silent.Dispose();
        }

        [Test]
        public void AClosingPeerIsNotSentAnythingElse()
        {
            var host = new Host(new SessionLimits { ReliableWindow = 1 });
            host.Connect();
            ClientSession first = host.Client;
            host.Connect();
            ulong closing = host.StartedPeers[0];
            ulong other = host.StartedPeers[1];
            host.Server.Send(closing, OrderId, new byte[] { 1 });
            host.Server.Flush();

            host.Server.Kick(closing);

            Assert.AreEqual(SendResult.NotConnected, host.Server.Send(closing, OrderId, new byte[] { 2 }));
            Assert.AreEqual(SendResult.NotConnected, host.Server.SendToObject(closing, OrderId, 1, 0, new byte[] { 2 }));
            Assert.AreEqual(1, host.Server.Broadcast(OrderId, new byte[] { 3 }));
            Assert.AreEqual(ConnectionState.Started, host.Server.PeerState(closing));
            Assert.AreEqual(ConnectionState.Started, host.Server.PeerState(other));
            first.Stop();
        }

        [Test]
        public void AClosingPeerIsNotQueuedObjectLayerMessages()
        {
            var host = new Host(new SessionLimits { ReliableWindow = 1 });
            ClientSession client = host.Connect();
            ulong peerId = host.StartedPeers.Single();
            host.Server.Send(peerId, OrderId, new byte[] { 1 });
            host.Server.Flush();
            host.Server.Kick(peerId);

            host.Server.EnqueueObjectMessage(peerId, OrderId, new byte[] { 2 });
            host.Server.EnqueueObjectMessageToStarted(OrderId, new byte[] { 3 });
            host.Server.Tick(host.Time + TimeSpan.FromSeconds(1));
            host.Server.Flush();

            Assert.AreEqual(1, host.PeerStops.Single().DiscardedMessages);
            client.Stop();
        }

        [Test]
        public void MessagesFromAClosingPeerAreNotDelivered()
        {
            var host = new Host();
            ClientSession client = host.Connect();
            ulong peerId = host.StartedPeers.Single();
            client.Send(OrderId, new byte[] { 1 });
            client.Flush();

            host.Server.Kick(peerId);
            host.Server.Tick(host.Time + TimeSpan.FromMilliseconds(16));

            CollectionAssert.IsEmpty(host.ServerOrders);
        }

        [Test]
        public void APeerThatCannotDrainIsClosedAtTheDeadline()
        {
            var host = new Host(new SessionLimits { ReliableWindow = 1, CloseTimeout = TimeSpan.FromSeconds(1) });
            ClientSession client = host.Connect();
            ulong peerId = host.StartedPeers.Single();
            host.Server.Send(peerId, OrderId, new byte[] { 1 });
            host.Server.Flush();
            host.Server.Kick(peerId);

            TimeSpan kickedAt = host.Time;
            host.Server.Tick(kickedAt + TimeSpan.FromMilliseconds(999));
            host.Server.Flush();
            Assert.AreEqual(0, host.PeerStops.Count);

            host.Server.Tick(kickedAt + TimeSpan.FromSeconds(1));
            host.Server.Flush();
            Assert.AreEqual(StopReason.LocalStop, host.PeerStops.Single().Reason);
            Assert.AreEqual(1, host.PeerStops.Single().DiscardedMessages);
            client.Stop();
        }

        [Test]
        public void AClosingPeerThatDisconnectsFirstStopsWithTheReasonOfTheClose()
        {
            var host = new Host(new SessionLimits { ReliableWindow = 1 });
            ClientSession client = host.Connect();
            ulong peerId = host.StartedPeers.Single();
            host.Server.Send(peerId, OrderId, new byte[] { 1 });
            host.Server.Flush();
            host.Server.Kick(peerId);

            client.Stop();
            host.Server.Tick(host.Time + TimeSpan.FromMilliseconds(16));

            Assert.AreEqual(StopReason.LocalStop, host.PeerStops.Single().Reason);
        }

        [Test]
        public void AServerHandlerExceptionSendsSessionCloseBeforeTheDisconnect()
        {
            var host = new Host();
            ClientSession client = host.Connect();

            client.Send(OrderId, new[] { Failing });
            host.Step(5);

            Assert.AreEqual(StopReason.HandlerException, host.PeerStops.Single().Reason);
            Assert.AreEqual(StopReason.ServerClosed, host.ClientStops.Single().Reason);
            Assert.AreEqual(SessionCloseReason.HandlerException, host.ClientStops.Single().CloseReason);
        }

        [Test]
        public void AnUnknownCloseReasonEndsWithServerClosedCarryingTheValue()
        {
            var host = new Host();
            host.Connect();

            host.Server.Send(host.StartedPeers.Single(), TestBundles.CloseId, Close(200));
            host.Step(5);

            Assert.AreEqual(StopReason.ServerClosed, host.ClientStops.Single().Reason);
            Assert.AreEqual((SessionCloseReason)200, host.ClientStops.Single().CloseReason);
        }

        [Test]
        public void TheClientSkipsWhatFollowsSessionCloseInTheSameReceive()
        {
            var host = new Host();
            ClientSession client = host.Connect();
            ulong peerId = host.StartedPeers.Single();

            host.Server.Send(peerId, OrderId, new byte[] { 1 });
            host.Server.Send(peerId, TestBundles.CloseId, Close(0));
            host.Server.Send(peerId, OrderId, new byte[] { 2 });
            host.Server.Flush();
            client.Tick(host.Time + TimeSpan.FromMilliseconds(16));

            CollectionAssert.AreEqual(new byte[] { 1 }, host.ClientOrders);
            Assert.AreEqual(StopReason.ServerClosed, host.ClientStops.Single().Reason);
            Assert.AreEqual(SessionCloseReason.Unspecified, host.ClientStops.Single().CloseReason);
        }

        [Test]
        public void TheServerDoesNotActOnASessionCloseFromAClient()
        {
            var host = new Host();
            ClientSession client = host.Connect();
            ulong peerId = host.StartedPeers.Single();

            client.Send(TestBundles.CloseId, Close(2));
            client.Send(OrderId, new byte[] { 1 });
            host.Step(5);

            CollectionAssert.AreEqual(new byte[] { 1 }, host.ServerOrders);
            Assert.AreEqual(ConnectionState.Started, host.Server.PeerState(peerId));
            Assert.AreEqual(ConnectionState.Started, client.State);
        }

        [Test]
        public void StoppingTheServerSendsNoSessionClose()
        {
            var host = new Host();
            host.Connect();

            host.Server.Stop();
            host.Step(5);

            Assert.AreEqual(StopReason.PeerClosed, host.ClientStops.Single().Reason);
        }
    }
}
