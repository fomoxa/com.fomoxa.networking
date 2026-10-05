using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class SessionTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const uint ObjectCallId = 0x2000_0002;

        private static Schema HostSchema() =>
            new Schema(0xCAFE, new[]
            {
                new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }),
                new MessageSchema(ObjectCallId, 0xF00E, new ulong[] { 0xF00E }),
            });

        private sealed class ThrowingTransport : ITransport
        {
            private readonly ITransport inner;

            public ThrowingTransport(ITransport inner)
            {
                this.inner = inner;
            }

            public bool Throws { get; set; }

            public bool Disposed { get; private set; }

            public TransportKind Kind => inner.Kind;

            public SendOutcome Send(ReadOnlySpan<byte> bytes) =>
                Throws ? throw new InvalidOperationException("send failed") : inner.Send(bytes);

            public ReceiveOutcome Receive(Span<byte> buffer) => inner.Receive(buffer);

            public void CloseGracefully() => inner.CloseGracefully();

            public void Dispose()
            {
                Disposed = true;
                inner.Dispose();
            }
        }

        private sealed class Host
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly List<ServerConnectionStateArgs> ServerStates = new List<ServerConnectionStateArgs>();
            public readonly List<ConnectionStateArgs> PeerStates = new List<ConnectionStateArgs>();
            public readonly List<ConnectionStateArgs> ClientStates = new List<ConnectionStateArgs>();
            public readonly List<(ulong PeerId, byte[] Payload)> ServerReceived = new List<(ulong, byte[])>();
            public readonly List<byte[]> ClientReceived = new List<byte[]>();
            public readonly List<(ulong PeerId, uint ObjectId, byte BehaviourIndex, byte[] Body)> ServerObjectCalls = new List<(ulong, uint, byte, byte[])>();
            public readonly List<(uint ObjectId, byte BehaviourIndex, byte[] Body)> ClientObjectCalls = new List<(uint, byte, byte[])>();
            public readonly ServerSession Server;
            public readonly ClientSession Client;
            public TimeSpan Now = TimeSpan.Zero;

            public Host(Schema clientSchema = null, SessionLimits clientLimits = null)
            {
                var serverDispatcher = new MessageDispatcher(HostSchema());
                var clientDispatcher = new MessageDispatcher(HostSchema());
                serverDispatcher.Register(GreetingId, (peerId, payload) => ServerReceived.Add((peerId, payload.ToArray())));
                clientDispatcher.Register(GreetingId, (peerId, payload) => ClientReceived.Add(payload.ToArray()));
                serverDispatcher.RegisterObject(ObjectCallId, (peerId, objectId, behaviourIndex, body) => ServerObjectCalls.Add((peerId, objectId, behaviourIndex, body.ToArray())));
                clientDispatcher.RegisterObject(ObjectCallId, (peerId, objectId, behaviourIndex, body) => ClientObjectCalls.Add((objectId, behaviourIndex, body.ToArray())));
                Server = new ServerSession(HostSchema(), new SessionConfig(), new SessionLimits(), serverDispatcher, TestBundles.Protocol());
                Client = new ClientSession(clientSchema ?? HostSchema(), new SessionConfig(), clientLimits ?? new SessionLimits(), clientDispatcher, TestBundles.Protocol());
                Server.OnServerConnectionState += ServerStates.Add;
                Server.OnRemoteConnectionState += PeerStates.Add;
                Client.OnClientConnectionState += ClientStates.Add;
                Server.Start(Listener);
            }

            public ulong OnlyPeerId => PeerStates[0].PeerId;

            public void ConnectClient()
            {
                Client.Start(Listener.Connect(), Now);
            }

            public void Step(int count)
            {
                for (int step = 0; step < count; step++)
                {
                    Now += TimeSpan.FromMilliseconds(16);
                    Server.Tick(Now);
                    Client.Tick(Now);
                    Server.Flush();
                    Client.Flush();
                }
            }
        }

        [Test]
        public void ClientAndPeerAreStartingDuringTheHandshakeAndStartedAtReady()
        {
            var host = new Host();
            Assert.AreEqual(new[] { ServerState.Starting, ServerState.Started }, host.ServerStates.ConvertAll(args => args.State).ToArray());

            host.ConnectClient();
            Assert.AreEqual(ConnectionState.Starting, host.Client.State);

            host.Step(20);

            Assert.AreEqual(new[] { ConnectionState.Starting, ConnectionState.Started }, host.ClientStates.ConvertAll(args => args.State).ToArray());
            Assert.AreEqual(new[] { ConnectionState.Starting, ConnectionState.Started }, host.PeerStates.ConvertAll(args => args.State).ToArray());
            Assert.AreEqual(ConnectionState.Started, host.Server.PeerState(host.OnlyPeerId));
        }

        [Test]
        public void MessageQueuedBeforeReadyIsSentOnceTheHandshakeCompletes()
        {
            var host = new Host();
            host.ConnectClient();

            Assert.AreEqual(SendResult.Queued, host.Client.Send(GreetingId, new byte[] { 42 }));
            host.Step(20);

            Assert.AreEqual(1, host.ServerReceived.Count);
            Assert.AreEqual(new byte[] { 42 }, host.ServerReceived[0].Payload);
            Assert.AreEqual(host.OnlyPeerId, host.ServerReceived[0].PeerId);
        }

        [Test]
        public void ServerSendReachesTheClientDispatcher()
        {
            var host = new Host();
            host.ConnectClient();
            host.Step(20);

            Assert.AreEqual(SendResult.Queued, host.Server.Send(host.OnlyPeerId, GreetingId, new byte[] { 7 }));
            host.Step(2);

            Assert.AreEqual(1, host.ClientReceived.Count);
            Assert.AreEqual(new byte[] { 7 }, host.ClientReceived[0]);
        }

        [Test]
        public void ObjectMessagesTravelBothWaysWithTheirAddress()
        {
            var host = new Host();
            host.ConnectClient();
            host.Step(20);

            Assert.AreEqual(SendResult.Queued, host.Client.SendToObject(ObjectCallId, 41, 1, new byte[] { 5 }));
            Assert.AreEqual(SendResult.Queued, host.Client.Send(GreetingId, new byte[] { 6 }));
            Assert.AreEqual(SendResult.Queued, host.Server.SendToObject(host.OnlyPeerId, ObjectCallId, 42, 3, new byte[0]));
            host.Step(2);

            Assert.AreEqual(1, host.ServerObjectCalls.Count);
            Assert.AreEqual(host.OnlyPeerId, host.ServerObjectCalls[0].PeerId);
            Assert.AreEqual(41u, host.ServerObjectCalls[0].ObjectId);
            Assert.AreEqual(1, host.ServerObjectCalls[0].BehaviourIndex);
            Assert.AreEqual(new byte[] { 5 }, host.ServerObjectCalls[0].Body);
            Assert.AreEqual(new byte[] { 6 }, host.ServerReceived[0].Payload);
            Assert.AreEqual(1, host.ClientObjectCalls.Count);
            Assert.AreEqual(42u, host.ClientObjectCalls[0].ObjectId);
            Assert.AreEqual(3, host.ClientObjectCalls[0].BehaviourIndex);
            Assert.AreEqual(0, host.ClientObjectCalls[0].Body.Length);
        }

        [Test]
        public void BroadcastToObjectReachesEveryStartedPeerWithTheAddress()
        {
            var host = new Host();
            Assert.AreEqual(0, host.Server.BroadcastToObject(ObjectCallId, 43, 2, new byte[] { 9 }));
            host.ConnectClient();
            host.Step(20);

            Assert.AreEqual(1, host.Server.BroadcastToObject(ObjectCallId, 43, 2, new byte[] { 9 }));
            host.Step(2);

            Assert.AreEqual(1, host.ClientObjectCalls.Count);
            Assert.AreEqual(43u, host.ClientObjectCalls[0].ObjectId);
            Assert.AreEqual(2, host.ClientObjectCalls[0].BehaviourIndex);
            Assert.AreEqual(new byte[] { 9 }, host.ClientObjectCalls[0].Body);
        }

        [Test]
        public void SendingWithoutASessionIsNotConnected()
        {
            var host = new Host();

            Assert.AreEqual(SendResult.NotConnected, host.Server.Send(99, GreetingId, new byte[] { 1 }));
            Assert.AreEqual(SendResult.NotConnected, host.Client.Send(GreetingId, new byte[] { 1 }));
            Assert.AreEqual(SendResult.NotConnected, host.Server.SendToObject(99, ObjectCallId, 1, 0, new byte[] { 1 }));
            Assert.AreEqual(SendResult.NotConnected, host.Client.SendToObject(ObjectCallId, 1, 0, new byte[] { 1 }));
        }

        [Test]
        public void ClientLimitsRefuseWithTheirReason()
        {
            var host = new Host(clientLimits: new SessionLimits { MessageCapacity = 1, ByteCapacity = 2 });
            host.ConnectClient();

            Assert.AreEqual(SendResult.TooLargeForQueue, host.Client.Send(GreetingId, new byte[3]));
            Assert.AreEqual(SendResult.Queued, host.Client.Send(GreetingId, new byte[1]));
            Assert.AreEqual(SendResult.Full, host.Client.Send(GreetingId, new byte[1]));
        }

        [Test]
        public void ServerDisconnectStopsThePeerWithLocalStopAndTheClientSeesPeerClosed()
        {
            var host = new Host();
            host.ConnectClient();
            host.Step(20);
            ulong peerId = host.OnlyPeerId;
            host.Server.Send(peerId, GreetingId, new byte[] { 1 });
            host.Server.Send(peerId, GreetingId, new byte[] { 2 });

            host.Server.Disconnect(peerId);

            ConnectionStateArgs stopped = host.PeerStates[host.PeerStates.Count - 1];
            Assert.AreEqual(ConnectionState.Stopped, stopped.State);
            Assert.AreEqual(StopReason.LocalStop, stopped.Reason);
            Assert.AreEqual(2, stopped.DiscardedMessages);
            Assert.AreEqual(SendResult.NotConnected, host.Server.Send(peerId, GreetingId, new byte[] { 3 }));

            host.Step(5);
            ConnectionStateArgs clientStopped = host.ClientStates[host.ClientStates.Count - 1];
            Assert.AreEqual(ConnectionState.Stopped, clientStopped.State);
            Assert.AreEqual(StopReason.PeerClosed, clientStopped.Reason);
        }

        [Test]
        public void AFailedLeaveStillStopsTheClientAndIsThrown()
        {
            var host = new Host();
            var transport = new ThrowingTransport(host.Listener.Connect());
            host.Client.Start(transport, host.Now);
            host.Step(20);
            Assert.AreEqual(ConnectionState.Started, host.Client.State);
            transport.Throws = true;

            Assert.Throws<InvalidOperationException>(host.Client.Stop);

            Assert.AreEqual(ConnectionState.Stopped, host.Client.State);
            ConnectionStateArgs stopped = host.ClientStates[host.ClientStates.Count - 1];
            Assert.AreEqual(ConnectionState.Stopped, stopped.State);
            Assert.AreEqual(StopReason.LocalStop, stopped.Reason);
            Assert.IsTrue(transport.Disposed);
            Assert.DoesNotThrow(host.Client.Stop);
        }

        [Test]
        public void ClientStopStopsWithLocalStopAndTheServerSeesPeerClosed()
        {
            var host = new Host();
            host.ConnectClient();
            host.Step(20);
            host.Client.Send(GreetingId, new byte[] { 1 });

            host.Client.Stop();

            ConnectionStateArgs stopped = host.ClientStates[host.ClientStates.Count - 1];
            Assert.AreEqual(StopReason.LocalStop, stopped.Reason);
            Assert.AreEqual(1, stopped.DiscardedMessages);

            host.Step(5);
            ConnectionStateArgs peerStopped = host.PeerStates[host.PeerStates.Count - 1];
            Assert.AreEqual(ConnectionState.Stopped, peerStopped.State);
            Assert.AreEqual(StopReason.PeerClosed, peerStopped.Reason);
            Assert.AreEqual(0, host.Server.PeerCount);
        }

        [Test]
        public void HandshakeFailureStopsTheClientWithTheFailure()
        {
            var conflicting = new Schema(0xBEEF, new[] { new MessageSchema(GreetingId, 0xDEAD, new ulong[] { 0xDEAD }) });
            var host = new Host(clientSchema: conflicting);
            host.ConnectClient();

            host.Step(20);

            ConnectionStateArgs stopped = host.ClientStates[host.ClientStates.Count - 1];
            Assert.AreEqual(ConnectionState.Stopped, stopped.State);
            Assert.AreEqual(StopReason.HandshakeFailed, stopped.Reason);
            Assert.AreEqual(HandshakeFailure.SchemaConflict, stopped.Failure);
            Assert.AreEqual(ConnectionState.Stopped, host.PeerStates[host.PeerStates.Count - 1].State);
        }

        [Test]
        public void ServerStopStopsEveryPeerBeforeItself()
        {
            var host = new Host();
            host.ConnectClient();
            host.Step(20);
            ulong peerId = host.OnlyPeerId;
            host.ServerStates.Clear();

            host.Server.Stop();

            Assert.AreEqual(new[] { ServerState.Stopping, ServerState.Stopped }, host.ServerStates.ConvertAll(args => args.State).ToArray());
            ConnectionStateArgs stopped = host.PeerStates[host.PeerStates.Count - 1];
            Assert.AreEqual(peerId, stopped.PeerId);
            Assert.AreEqual(StopReason.LocalStop, stopped.Reason);
            Assert.AreEqual(0, host.Server.PeerCount);
        }
    }
}
