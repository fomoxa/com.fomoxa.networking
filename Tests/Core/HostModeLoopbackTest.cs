using System;
using System.Collections.Generic;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class HostModeLoopbackTest
    {
        private const uint GreetingId = 0x2000_0001;

        private static Schema HostSchema() =>
            new Schema(0xCAFE, new[] { new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }) });

        [Test]
        public void LocalClientReachesReadyAndItsMessageIsDispatchedOnTheServer()
        {
            var loopback = new LoopbackListener(64);
            using var server = new FomoxaServer(new CompositeListener(loopback), HostSchema(), new SessionConfig());
            using var client = FomoxaConnection.Connect(loopback.Connect(), HostSchema(), new SessionConfig(), TimeSpan.Zero);

            var dispatcher = new MessageDispatcher(HostSchema());
            var received = new List<byte[]>();
            dispatcher.Register(GreetingId, (peerId, payload) => received.Add(payload.ToArray()));

            bool serverSawReady = false;
            TimeSpan now = TimeSpan.Zero;
            for (int step = 0; step < 20 && !(serverSawReady && client.IsReady); step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                foreach (FomoxaEvent raised in server.Tick(now))
                {
                    serverSawReady |= raised.Kind == FomoxaEventKind.Ready;
                }

                client.Tick(now);
            }

            Assert.IsTrue(serverSawReady);
            Assert.IsTrue(client.IsReady);

            Assert.AreEqual(SendStatus.Sent, client.Send(GreetingId, new byte[] { 42 }));
            now += TimeSpan.FromMilliseconds(16);
            foreach (FomoxaEvent raised in server.Tick(now))
            {
                if (raised.Kind == FomoxaEventKind.Message)
                {
                    dispatcher.Dispatch(raised.PeerId, raised.MessageId, raised.Payload);
                }
            }

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(new byte[] { 42 }, received[0]);
        }
    }
}
