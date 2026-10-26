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
    public sealed class PeerTransferTest
    {
        private const uint OrderId = 0x2000_0003;
        private static readonly byte[] Ticket = { 7, 7, 7 };

        private static Schema HostSchema() =>
            new Schema(0xCAFE, new[] { new MessageSchema(OrderId, 0xF00D, new ulong[] { 0xF00D }) });

        private static MessageChannels Channels()
        {
            var channels = new MessageChannels();
            channels.Set(OrderId, Channel.ReliableOrdered);
            return channels;
        }

        private static byte[] Transfer(string address, ushort port, byte[] ticket) =>
            PeerTransferNetAdapter.Instance.Encode(new PeerTransfer { Address = address, Port = port, Ticket = ticket }).ToArray();

        private sealed class Host
        {
            public readonly LoopbackListener Listener = new LoopbackListener(256);
            public readonly LoopbackListener LocalListener = new LoopbackListener(256);
            public readonly List<byte> ServerOrders = new List<byte>();
            public readonly List<byte> ClientOrders = new List<byte>();
            public readonly List<ConnectionStateArgs> PeerStops = new List<ConnectionStateArgs>();
            public readonly List<ConnectionStateArgs> ClientStates = new List<ConnectionStateArgs>();
            public readonly List<string> Transfers = new List<string>();
            public readonly List<ReceiveDroppedArgs> ClientDropped = new List<ReceiveDroppedArgs>();
            public readonly List<ulong> StartedPeers = new List<ulong>();
            public readonly ServerSession Server;
            public TimeSpan Time = TimeSpan.Zero;

            public Host(SessionLimits limits = null)
            {
                var dispatcher = new MessageDispatcher(HostSchema());
                dispatcher.Register(OrderId, (peerId, payload) => ServerOrders.Add(payload.Span[0]));
                Server = new ServerSession(HostSchema(), new SessionConfig(), limits ?? new SessionLimits(), dispatcher, TestBundles.Protocol(Channels())).SendingLocalPeer();
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
                Server.Start(new CompositeListener(Listener, LocalListener));
                Server.LocalListenerIndex = 1;
            }

            public ClientSession Client { get; private set; }

            public Action<string, ushort> OnTransferring { get; set; }

            public ClientSession Connect(LoopbackListener listener = null)
            {
                var dispatcher = new MessageDispatcher(HostSchema());
                dispatcher.Register(OrderId, (peerId, payload) => ClientOrders.Add(payload.Span[0]));
                Client = new ClientSession(HostSchema(), new SessionConfig(), new SessionLimits(), dispatcher, TestBundles.Protocol(Channels())).AcceptingLocalPeer();
                ClientSession client = Client;
                Client.OnClientConnectionState += ClientStates.Add;
                Client.OnReceiveDropped += ClientDropped.Add;
                Client.SetTransferringHandler((address, port, connection) =>
                {
                    Transfers.Add($"{address}:{port} {client.State}");
                    OnTransferring?.Invoke(address, port);
                });
                Client.Start((listener ?? Listener).Connect(), Time);
                Step(20);
                return Client;
            }

            public List<ConnectionStateArgs> ClientStops() => ClientStates.FindAll(args => args.State == ConnectionState.Stopped);

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
        public void ATransferredClientAndItsPeerBothEndWithTransferred()
        {
            var host = new Host();
            host.Connect();
            ulong peerId = host.StartedPeers.Single();

            Assert.IsTrue(host.Server.Transfer(peerId, "arena-2.example", 7778, Ticket));
            host.Step(5);

            Assert.AreEqual(StopReason.Transferred, host.ClientStops().Single().Reason);
            Assert.AreEqual(SessionCloseReason.Unspecified, host.ClientStops().Single().CloseReason);
            Assert.AreEqual(StopReason.Transferred, host.PeerStops.Single().Reason);
            Assert.AreEqual(0, host.Server.PeerCount);
        }

        [Test]
        public void TheTransferringHandlerSeesTheTargetWhileTheClientIsStillStarted()
        {
            var host = new Host();
            host.Connect();

            host.Server.Transfer(host.StartedPeers.Single(), "arena-2.example", 7778, Ticket);
            host.Step(5);

            CollectionAssert.AreEqual(new[] { "arena-2.example:7778 Started" }, host.Transfers);
        }

        [Test]
        public void TheTicketReachesTheNextAuthenticationOnlyUntilItStarts()
        {
            var host = new Host();
            ClientSession client = host.Connect();
            var seen = new List<byte[]>();
            client.SetAuthenticatingHandler(context => seen.Add(context.Ticket.ToArray()));

            host.Server.Transfer(host.StartedPeers.Single(), "arena-2.example", 7778, Ticket);
            host.Step(5);
            client.Start(host.Listener.Connect(), host.Time);
            host.Step(20);
            Assert.AreEqual(ConnectionState.Started, client.State);
            client.Stop();
            client.Start(host.Listener.Connect(), host.Time);
            host.Step(20);

            Assert.AreEqual(2, seen.Count);
            CollectionAssert.AreEqual(Ticket, seen[0]);
            CollectionAssert.IsEmpty(seen[1]);
        }

        [Test]
        public void AForgottenTicketDoesNotReachTheNextAuthentication()
        {
            var host = new Host();
            ClientSession client = host.Connect();
            var seen = new List<byte[]>();
            client.SetAuthenticatingHandler(context => seen.Add(context.Ticket.ToArray()));

            host.Server.Transfer(host.StartedPeers.Single(), "arena-2.example", 7778, Ticket);
            host.Step(5);
            client.ForgetTransferTicket();
            client.Start(host.Listener.Connect(), host.Time);
            host.Step(20);

            CollectionAssert.IsEmpty(seen.Single());
        }

        [Test]
        public void MessagesQueuedBeforeATransferArriveBeforeIt()
        {
            var host = new Host();
            host.Connect();
            ulong peerId = host.StartedPeers.Single();

            host.Server.Send(peerId, OrderId, new byte[] { 1 });
            host.Server.Send(peerId, OrderId, new byte[] { 2 });
            host.Server.Transfer(peerId, "arena-2.example", 7778, Ticket);
            host.Step(5);

            CollectionAssert.AreEqual(new byte[] { 1, 2 }, host.ClientOrders);
            Assert.AreEqual(StopReason.Transferred, host.ClientStops().Single().Reason);
        }

        [Test]
        public void TransferReturnsFalseForAnUnknownPeerAndForAPeerAlreadyClosing()
        {
            var host = new Host(new SessionLimits { ReliableWindow = 1 });
            host.Connect();
            ulong peerId = host.StartedPeers.Single();
            host.Server.Send(peerId, OrderId, new byte[] { 1 });
            host.Server.Flush();

            Assert.IsFalse(host.Server.Transfer(peerId + 100, "arena-2.example", 7778, Ticket));
            Assert.IsTrue(host.Server.Transfer(peerId, "arena-2.example", 7778, Ticket));
            host.Server.Flush();
            Assert.AreEqual(0, host.PeerStops.Count);
            Assert.IsFalse(host.Server.Transfer(peerId, "arena-2.example", 7778, Ticket));
            Assert.IsFalse(host.Server.Kick(peerId));
        }

        [Test]
        public void TransferReturnsFalseForAPeerThatHasNotStarted()
        {
            var host = new Host();
            var silent = host.Listener.Connect();
            host.Server.Tick(TimeSpan.FromMilliseconds(16));
            ulong peerId = 1;
            Assert.AreEqual(ConnectionState.Starting, host.Server.PeerState(peerId));

            Assert.IsFalse(host.Server.Transfer(peerId, "arena-2.example", 7778, Ticket));

            Assert.AreEqual(ConnectionState.Starting, host.Server.PeerState(peerId));
            Assert.AreEqual(0, host.PeerStops.Count);
            silent.Dispose();
        }

        [Test]
        public void ANullAddressIsRejectedBeforeAnythingIsSent()
        {
            var host = new Host();
            host.Connect();
            ulong peerId = host.StartedPeers.Single();

            Assert.Throws<ArgumentNullException>(() => host.Server.Transfer(peerId, null, 7778, Ticket));

            Assert.AreEqual(SendResult.Queued, host.Server.Send(peerId, OrderId, new byte[] { 1 }));
        }

        [Test]
        public void ThePeerOfTheLocalClientCannotBeTransferred()
        {
            var host = new Host();
            host.Connect(host.LocalListener);
            ulong peerId = host.StartedPeers.Single();

            Assert.Throws<InvalidOperationException>(() => host.Server.Transfer(peerId, "arena-2.example", 7778, Ticket));

            host.Step(5);
            Assert.AreEqual(ConnectionState.Started, host.Server.PeerState(peerId));
            Assert.AreEqual(ConnectionState.Started, host.Client.State);
        }

        [Test]
        public void APeerOfTheNetworkListenerCanBeTransferredNextToALocalClient()
        {
            var host = new Host();
            ClientSession local = host.Connect(host.LocalListener);
            host.Connect();
            ulong remote = host.StartedPeers[1];

            Assert.IsTrue(host.Server.Transfer(remote, "arena-2.example", 7778, Ticket));
            local.Stop();
        }

        [Test]
        public void ATransferredPeerIsNotSentAnythingElse()
        {
            var host = new Host(new SessionLimits { ReliableWindow = 1 });
            host.Connect();
            ulong peerId = host.StartedPeers.Single();
            host.Server.Send(peerId, OrderId, new byte[] { 1 });
            host.Server.Flush();

            host.Server.Transfer(peerId, "arena-2.example", 7778, Ticket);

            Assert.AreEqual(SendResult.NotConnected, host.Server.Send(peerId, OrderId, new byte[] { 2 }));
            Assert.AreEqual(SendResult.NotConnected, host.Server.SendToObject(peerId, OrderId, 1, 0, new byte[] { 2 }));
            Assert.AreEqual(0, host.Server.Broadcast(OrderId, new byte[] { 3 }));
        }

        [Test]
        public void TheClientSkipsWhatFollowsPeerTransferInTheSameReceive()
        {
            var host = new Host();
            ClientSession client = host.Connect();
            ulong peerId = host.StartedPeers.Single();

            host.Server.Send(peerId, OrderId, new byte[] { 1 });
            host.Server.Send(peerId, TestBundles.TransferId, Transfer("arena-2.example", 7778, Ticket));
            host.Server.Send(peerId, OrderId, new byte[] { 2 });
            host.Server.Send(peerId, TestBundles.CloseId, SessionCloseNetAdapter.Instance.Encode(new SessionClose { Reason = 2 }).ToArray());
            host.Server.Flush();
            client.Tick(host.Time + TimeSpan.FromMilliseconds(16));

            CollectionAssert.AreEqual(new byte[] { 1 }, host.ClientOrders);
            Assert.AreEqual(StopReason.Transferred, host.ClientStops().Single().Reason);
            Assert.AreEqual(1, host.Transfers.Count);
        }

        [Test]
        public void ATransferredCloseWithoutPeerTransferEndsWithServerClosed()
        {
            var host = new Host();
            host.Connect();

            host.Server.Send(host.StartedPeers.Single(), TestBundles.CloseId, SessionCloseNetAdapter.Instance.Encode(new SessionClose { Reason = 5 }).ToArray());
            host.Step(5);

            Assert.AreEqual(StopReason.ServerClosed, host.ClientStops().Single().Reason);
            Assert.AreEqual(SessionCloseReason.Transferred, host.ClientStops().Single().CloseReason);
            CollectionAssert.IsEmpty(host.Transfers);
        }

        [Test]
        public void StoppingInTheTransferringHandlerEndsWithLocalStop()
        {
            var host = new Host();
            ClientSession client = host.Connect();
            host.OnTransferring = (address, port) => client.Stop();

            host.Server.Transfer(host.StartedPeers.Single(), "arena-2.example", 7778, Ticket);
            host.Step(5);

            Assert.AreEqual(StopReason.LocalStop, host.ClientStops().Single().Reason);
            Assert.AreEqual(ConnectionState.Stopped, client.State);
        }

        [Test]
        public void AMalformedPeerTransferIsDroppedAndTheSessionGoesOn()
        {
            var host = new Host();
            ClientSession client = host.Connect();
            ulong peerId = host.StartedPeers.Single();

            host.Server.Send(peerId, TestBundles.TransferId, new byte[] { 0xFF });
            host.Server.Send(peerId, OrderId, new byte[] { 1 });
            host.Step(5);

            Assert.AreEqual(1, host.ClientDropped.Count);
            CollectionAssert.IsEmpty(host.Transfers);
            CollectionAssert.AreEqual(new byte[] { 1 }, host.ClientOrders);
            Assert.AreEqual(ConnectionState.Started, client.State);
        }

        [Test]
        public void TheServerDoesNotActOnAPeerTransferFromAClient()
        {
            var host = new Host();
            ClientSession client = host.Connect();
            ulong peerId = host.StartedPeers.Single();

            client.Send(TestBundles.TransferId, Transfer("arena-2.example", 7778, Ticket));
            client.Send(OrderId, new byte[] { 1 });
            host.Step(5);

            CollectionAssert.AreEqual(new byte[] { 1 }, host.ServerOrders);
            Assert.AreEqual(ConnectionState.Started, host.Server.PeerState(peerId));
            Assert.AreEqual(ConnectionState.Started, client.State);
        }
    }
}
