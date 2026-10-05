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
    public sealed class HandlerExceptionTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const byte Failing = 0xFF;

        private static Schema TestSchema() =>
            new Schema(0xCAFE, new[] { new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }) });

        private static MessageDispatcher FailingDispatcher(List<(ulong PeerId, byte Value)> received, Action onFailing = null)
        {
            var dispatcher = new MessageDispatcher(TestSchema());
            dispatcher.Register(GreetingId, (peerId, payload) =>
            {
                byte value = payload.Span[0];
                if (value == Failing)
                {
                    onFailing?.Invoke();
                    throw new InvalidOperationException("handler failed");
                }

                received.Add((peerId, value));
            });
            return dispatcher;
        }

        private static void SendBundle(FomoxaConnection client, params byte[] values)
        {
            var bundle = new MessageBundle();
            foreach (byte value in values)
            {
                bundle.Entries.Add(new BundleEntry { MessageId = GreetingId, Data = new[] { value } });
            }

            Assert.AreEqual(SendStatus.Sent, client.Send(TestBundles.MessageId, MessageBundleNetAdapter.Instance.Encode(bundle).Span));
        }

        [Test]
        public void ServerDisconnectsOnlyThePeerWhoseMessageFailedAndKeepsDeliveringToOthers()
        {
            var listener = new LoopbackListener(64);
            var received = new List<(ulong PeerId, byte Value)>();
            var server = new ServerSession(TestSchema(), new SessionConfig(), new SessionLimits(), FailingDispatcher(received), TestBundles.Protocol());
            var exceptions = new List<HandlerExceptionArgs>();
            var stopped = new List<ConnectionStateArgs>();
            server.OnHandlerException += exceptions.Add;
            server.OnRemoteConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    stopped.Add(args);
                }
            };
            server.Start(listener);
            TimeSpan now = TimeSpan.Zero;
            FomoxaConnection failingClient = FomoxaConnection.Connect(listener.Connect(), TestSchema(), new SessionConfig(), now);
            FomoxaConnection otherClient = FomoxaConnection.Connect(listener.Connect(), TestSchema(), new SessionConfig(), now);
            for (int step = 0; step < 20; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                server.Tick(now);
                failingClient.Tick(now);
                otherClient.Tick(now);
            }

            SendBundle(failingClient, Failing, 1);
            SendBundle(otherClient, 2);
            server.Tick(now + TimeSpan.FromMilliseconds(16));

            Assert.AreEqual(1, exceptions.Count);
            Assert.AreEqual(GreetingId, exceptions[0].MessageId);
            Assert.IsInstanceOf<InvalidOperationException>(exceptions[0].Exception);
            Assert.AreEqual(1, stopped.Count);
            Assert.AreEqual(exceptions[0].PeerId, stopped[0].PeerId);
            Assert.AreEqual(StopReason.HandlerException, stopped[0].Reason);
            Assert.AreEqual(1, received.Count);
            Assert.AreNotEqual(exceptions[0].PeerId, received[0].PeerId);
            Assert.AreEqual(2, received[0].Value);
            Assert.AreEqual(ServerState.Started, server.State);
            Assert.AreEqual(1, server.PeerCount);
            failingClient.Dispose();
            otherClient.Dispose();
            server.Stop();
        }

        [Test]
        public void ClientStopsWithHandlerExceptionAndSkipsTheRestOfTheBundle()
        {
            var listener = new LoopbackListener(64);
            var server = new ServerSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
            ulong peerId = 0;
            server.OnRemoteConnectionState += args => peerId = args.PeerId;
            server.Start(listener);
            var received = new List<(ulong PeerId, byte Value)>();
            var client = new ClientSession(TestSchema(), new SessionConfig(), new SessionLimits(), FailingDispatcher(received), TestBundles.Protocol());
            var exceptions = new List<HandlerExceptionArgs>();
            var states = new List<ConnectionStateArgs>();
            client.OnHandlerException += exceptions.Add;
            client.OnClientConnectionState += states.Add;
            TimeSpan now = TimeSpan.Zero;
            client.Start(listener.Connect(), now);
            for (int step = 0; step < 20 && client.State != ConnectionState.Started; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                server.Tick(now);
                client.Tick(now);
            }

            Assert.AreEqual(ConnectionState.Started, client.State);
            Assert.AreEqual(SendResult.Queued, server.Send(peerId, GreetingId, new[] { Failing }));
            Assert.AreEqual(SendResult.Queued, server.Send(peerId, GreetingId, new byte[] { 1 }));
            server.Flush();
            for (int step = 0; step < 20 && server.PeerCount > 0; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                client.Tick(now);
                server.Tick(now);
            }

            Assert.AreEqual(1, exceptions.Count);
            Assert.AreEqual(GreetingId, exceptions[0].MessageId);
            Assert.AreEqual(0, received.Count);
            Assert.AreEqual(ConnectionState.Stopped, client.State);
            Assert.AreEqual(StopReason.HandlerException, states[states.Count - 1].Reason);
            Assert.AreEqual(0, server.PeerCount);
            server.Stop();
        }

        [Test]
        public void UndecodableModelInAHandlerDropsOnlyThatEntry()
        {
            const byte Undecodable = 0xEE;
            var listener = new LoopbackListener(64);
            var server = new ServerSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
            ulong peerId = 0;
            server.OnRemoteConnectionState += args => peerId = args.PeerId;
            server.Start(listener);
            var received = new List<byte>();
            var dispatcher = new MessageDispatcher(TestSchema());
            dispatcher.Register(GreetingId, (sender, payload) =>
            {
                if (payload.Span[0] == Undecodable)
                {
                    throw new MessageDecodeException("model ended inside a field", null);
                }

                received.Add(payload.Span[0]);
            });
            var client = new ClientSession(TestSchema(), new SessionConfig(), new SessionLimits(), dispatcher, TestBundles.Protocol());
            var exceptions = new List<HandlerExceptionArgs>();
            var dropped = new List<ReceiveDroppedArgs>();
            client.OnHandlerException += exceptions.Add;
            client.OnReceiveDropped += dropped.Add;
            TimeSpan now = TimeSpan.Zero;
            client.Start(listener.Connect(), now);
            for (int step = 0; step < 20 && client.State != ConnectionState.Started; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                server.Tick(now);
                client.Tick(now);
            }

            server.Send(peerId, GreetingId, new byte[] { 1 });
            server.Send(peerId, GreetingId, new[] { Undecodable });
            server.Send(peerId, GreetingId, new byte[] { 2 });
            server.Flush();
            now += TimeSpan.FromMilliseconds(16);
            client.Tick(now);

            Assert.AreEqual(new byte[] { 1, 2 }, received.ToArray());
            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual(1, dropped[0].FrameLength);
            Assert.AreEqual(0, exceptions.Count);
            Assert.AreEqual(ConnectionState.Started, client.State);
            client.Stop();
            server.Stop();
        }

        [Test]
        public void HandlerThatStopsTheClientBeforeThrowingEndsTheSessionOnce()
        {
            var listener = new LoopbackListener(64);
            var server = new ServerSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
            ulong peerId = 0;
            server.OnRemoteConnectionState += args => peerId = args.PeerId;
            server.Start(listener);
            var received = new List<(ulong PeerId, byte Value)>();
            ClientSession client = null;
            client = new ClientSession(TestSchema(), new SessionConfig(), new SessionLimits(), FailingDispatcher(received, () => client.Stop()), TestBundles.Protocol());
            var exceptions = new List<HandlerExceptionArgs>();
            var stopped = new List<ConnectionStateArgs>();
            client.OnHandlerException += exceptions.Add;
            client.OnClientConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    stopped.Add(args);
                }
            };
            TimeSpan now = TimeSpan.Zero;
            client.Start(listener.Connect(), now);
            for (int step = 0; step < 20 && client.State != ConnectionState.Started; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                server.Tick(now);
                client.Tick(now);
            }

            server.Send(peerId, GreetingId, new[] { Failing });
            server.Flush();
            now += TimeSpan.FromMilliseconds(16);
            client.Tick(now);

            Assert.AreEqual(1, exceptions.Count);
            Assert.AreEqual(1, stopped.Count);
            Assert.AreEqual(StopReason.LocalStop, stopped[0].Reason);
            server.Stop();
        }
    }
}
