using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ServerBroadcastTest
    {
        private const uint GreetingId = 0x2000_0001;

        private static Schema TestSchema() =>
            new Schema(0xCAFE, new[] { new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }) });

        private sealed class Network
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly ServerSession Server;
            public readonly List<ClientSession> Clients = new List<ClientSession>();
            public readonly List<List<byte>> Received = new List<List<byte>>();
            public TimeSpan Now = TimeSpan.Zero;

            public Network(SessionLimits serverLimits = null)
            {
                Server = new ServerSession(TestSchema(), new SessionConfig(), serverLimits ?? new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
                Server.Start(Listener);
            }

            public ClientSession Connect()
            {
                var received = new List<byte>();
                var dispatcher = new MessageDispatcher(TestSchema());
                dispatcher.Register(GreetingId, (peerId, payload) => received.Add(payload.Span[0]));
                var client = new ClientSession(TestSchema(), new SessionConfig(), new SessionLimits(), dispatcher, TestBundles.Protocol());
                client.Start(Listener.Connect(), Now);
                Clients.Add(client);
                Received.Add(received);
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

        [Test]
        public void BroadcastReachesEveryStartedPeerAndCountsThem()
        {
            var network = new Network();
            network.Connect();
            network.Connect();
            network.Step(20);

            Assert.AreEqual(2, network.Server.Broadcast(GreetingId, new byte[] { 7 }));
            network.Step(3);

            Assert.AreEqual(new byte[] { 7 }, network.Received[0].ToArray());
            Assert.AreEqual(new byte[] { 7 }, network.Received[1].ToArray());
        }

        [Test]
        public void PeerStillInTheHandshakeIsSkipped()
        {
            var network = new Network();
            network.Connect();
            network.Step(20);
            using LoopbackTransport silentPeer = network.Listener.Connect();
            network.Step(1);
            Assert.AreEqual(2, network.Server.PeerCount);

            Assert.AreEqual(1, network.Server.Broadcast(GreetingId, new byte[] { 7 }));
            network.Step(3);

            Assert.AreEqual(new byte[] { 7 }, network.Received[0].ToArray());
        }

        [Test]
        public void PeerWhoseQueueRefusesIsSkippedAndNotCounted()
        {
            var network = new Network(new SessionLimits { MessageCapacity = 1 });
            network.Connect();
            network.Step(20);

            Assert.AreEqual(1, network.Server.Broadcast(GreetingId, new byte[] { 1 }));
            Assert.AreEqual(0, network.Server.Broadcast(GreetingId, new byte[] { 2 }));
            network.Step(3);

            Assert.AreEqual(new byte[] { 1 }, network.Received[0].ToArray());
        }

        [Test]
        public void BroadcastWithoutPeersQueuesNothing()
        {
            var network = new Network();

            Assert.AreEqual(0, network.Server.Broadcast(GreetingId, new byte[] { 1 }));
        }
    }
}
