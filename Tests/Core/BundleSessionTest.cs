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
    public sealed class BundleSessionTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const uint ObjectCallId = 0x2000_0002;

        private static Schema HostSchema() =>
            new Schema(0xCAFE, new[]
            {
                new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }),
                new MessageSchema(ObjectCallId, 0xF00E, new ulong[] { 0xF00E }),
            });

        private sealed class RawClientHost : IDisposable
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly List<byte[]> ServerReceived = new List<byte[]>();
            public readonly List<ReceiveDroppedArgs> Dropped = new List<ReceiveDroppedArgs>();
            public readonly ServerSession Server;
            public readonly FomoxaConnection Client;
            public TimeSpan Now = TimeSpan.Zero;

            public RawClientHost(Action<ServerSession, ulong, ReadOnlyMemory<byte>> onGreeting = null)
            {
                var dispatcher = new MessageDispatcher(HostSchema());
                dispatcher.Register(GreetingId, (peerId, payload) =>
                {
                    ServerReceived.Add(payload.ToArray());
                    onGreeting?.Invoke(Server, peerId, payload);
                });
                dispatcher.RegisterObject(ObjectCallId, (peerId, objectId, behaviourIndex, body) => ServerReceived.Add(body.ToArray()));
                Server = new ServerSession(HostSchema(), new SessionConfig(), new SessionLimits(), dispatcher, TestBundles.Protocol());
                Server.OnReceiveDropped += Dropped.Add;
                Server.Start(Listener);
                Client = FomoxaConnection.Connect(Listener.Connect(), HostSchema(), new SessionConfig(), Now);
                Step(20);
            }

            public void Step(int count)
            {
                for (int step = 0; step < count; step++)
                {
                    Now += TimeSpan.FromMilliseconds(16);
                    Server.Tick(Now);
                    Client.Tick(Now);
                }
            }

            public void SendBundle(params byte[][] greetings)
            {
                var bundle = new MessageBundle();
                foreach (byte[] greeting in greetings)
                {
                    bundle.Entries.Add(new BundleEntry { MessageId = GreetingId, Data = greeting });
                }

                Assert.AreEqual(SendStatus.Sent, Client.Send(TestBundles.MessageId, MessageBundleNetAdapter.Instance.Encode(bundle).Span));
            }

            public void Dispose()
            {
                Client.Dispose();
                Server.Stop();
            }
        }

        [Test]
        public void EveryEntryOfABundleIsDispatchedInOrder()
        {
            using var host = new RawClientHost();

            host.SendBundle(new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });
            host.Step(1);

            Assert.AreEqual(3, host.ServerReceived.Count);
            Assert.AreEqual(new byte[] { 3 }, host.ServerReceived[2]);
            Assert.AreEqual(0, host.Dropped.Count);
        }

        [Test]
        public void FrameThatIsNotABundleIsDroppedReportedAndTheSessionContinues()
        {
            using var host = new RawClientHost();

            Assert.AreEqual(SendStatus.Sent, host.Client.Send(GreetingId, new byte[] { 9 }));
            host.Step(1);

            Assert.AreEqual(0, host.ServerReceived.Count);
            Assert.AreEqual(1, host.Dropped.Count);
            Assert.AreEqual(FomoxaWire.DataFrameHeaderSize + 1, host.Dropped[0].FrameLength);

            host.SendBundle(new byte[] { 1 });
            host.Step(1);
            Assert.AreEqual(1, host.ServerReceived.Count);
        }

        [Test]
        public void BundleThatDoesNotDecodeIsDroppedWithoutDispatchingAnyEntry()
        {
            using var host = new RawClientHost();
            var bundle = new MessageBundle();
            bundle.Entries.Add(new BundleEntry { MessageId = GreetingId, Data = new byte[] { 1 } });
            bundle.Entries.Add(new BundleEntry { MessageId = GreetingId, Data = new byte[] { 2, 3 } });
            byte[] encoded = MessageBundleNetAdapter.Instance.Encode(bundle).ToArray();

            Assert.AreEqual(SendStatus.Sent, host.Client.Send(TestBundles.MessageId, encoded.AsSpan(0, encoded.Length - 1)));
            host.Step(1);

            Assert.AreEqual(0, host.ServerReceived.Count);
            Assert.AreEqual(1, host.Dropped.Count);
            Assert.AreEqual(1, host.Server.PeerCount);
        }

        [Test]
        public void ObjectEntryShorterThanTheHeaderDropsTheWholeBundle()
        {
            using var host = new RawClientHost();
            var bundle = new MessageBundle();
            bundle.Entries.Add(new BundleEntry { MessageId = GreetingId, Data = new byte[] { 1 } });
            bundle.Entries.Add(new BundleEntry { MessageId = ObjectCallId, Data = new byte[ObjectHeader.UnreliableLength - 1] });

            Assert.AreEqual(SendStatus.Sent, host.Client.Send(TestBundles.MessageId, MessageBundleNetAdapter.Instance.Encode(bundle).Span));
            host.Step(1);

            Assert.AreEqual(0, host.ServerReceived.Count);
            Assert.AreEqual(1, host.Dropped.Count);
            Assert.AreEqual(1, host.Server.PeerCount);
        }

        [Test]
        public void HandlerThatDisconnectsThePeerStopsTheRestOfTheBundle()
        {
            using var host = new RawClientHost((server, peerId, payload) => server.Disconnect(peerId));

            host.SendBundle(new byte[] { 1 }, new byte[] { 2 });
            host.Step(1);

            Assert.AreEqual(1, host.ServerReceived.Count);
            Assert.AreEqual(0, host.Server.PeerCount);
        }

        [Test]
        public void MessagesSentInOneTickTravelInOneFrame()
        {
            var listener = new LoopbackListener(64);
            var server = new ServerSession(HostSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(HostSchema()), TestBundles.Protocol());
            server.Start(listener);
            using var client = FomoxaConnection.Connect(listener.Connect(), HostSchema(), new SessionConfig(), TimeSpan.Zero);
            TimeSpan now = TimeSpan.Zero;
            ulong peerId = 0;
            server.OnRemoteConnectionState += args => peerId = args.PeerId;
            for (int step = 0; step < 20 && !client.IsReady; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                server.Tick(now);
                client.Tick(now);
            }

            server.Send(peerId, GreetingId, new byte[] { 1 });
            server.Send(peerId, GreetingId, new byte[] { 2 });
            server.Flush();
            now += TimeSpan.FromMilliseconds(16);

            var frames = new List<uint>();
            foreach (FomoxaEvent raised in client.Tick(now))
            {
                if (raised.Kind == FomoxaEventKind.Message)
                {
                    frames.Add(raised.MessageId);
                }
            }

            Assert.AreEqual(new[] { TestBundles.MessageId }, frames.ToArray());
            server.Stop();
        }
    }
}
