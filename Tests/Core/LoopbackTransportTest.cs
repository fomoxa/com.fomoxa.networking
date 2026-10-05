using System;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class LoopbackTransportTest
    {
        [Test]
        public void EachSendArrivesAsOneWholePacketInOrder()
        {
            LoopbackTransport.CreatePair(8, out LoopbackTransport client, out LoopbackTransport server);
            client.Send(new byte[] { 1, 2, 3 });
            client.Send(new byte[] { 4 });

            var buffer = new byte[16];
            ReceiveOutcome first = server.Receive(buffer);
            Assert.AreEqual(TransportSignal.Ok, first.Signal);
            Assert.AreEqual(new byte[] { 1, 2, 3 }, buffer.AsSpan(0, first.Count).ToArray());

            ReceiveOutcome second = server.Receive(buffer);
            Assert.AreEqual(TransportSignal.Ok, second.Signal);
            Assert.AreEqual(new byte[] { 4 }, buffer.AsSpan(0, second.Count).ToArray());

            Assert.AreEqual(TransportSignal.WouldBlock, server.Receive(buffer).Signal);
        }

        [Test]
        public void SmallBufferAsksForCapacityWithoutLosingThePacket()
        {
            LoopbackTransport.CreatePair(8, out LoopbackTransport client, out LoopbackTransport server);
            client.Send(new byte[] { 9, 9, 9, 9 });

            ReceiveOutcome tooSmall = server.Receive(new byte[2]);
            Assert.AreEqual(TransportSignal.NeedCapacity, tooSmall.Signal);
            Assert.AreEqual(4, tooSmall.Count);

            ReceiveOutcome retried = server.Receive(new byte[4]);
            Assert.AreEqual(TransportSignal.Ok, retried.Signal);
            Assert.AreEqual(4, retried.Count);
        }

        [Test]
        public void FullQueueDropsTheOldestPacket()
        {
            LoopbackTransport.CreatePair(2, out LoopbackTransport client, out LoopbackTransport server);
            client.Send(new byte[] { 1 });
            client.Send(new byte[] { 2 });
            client.Send(new byte[] { 3 });

            Assert.AreEqual(2, server.QueuedPacketCount);
            var buffer = new byte[1];
            server.Receive(buffer);
            Assert.AreEqual(2, buffer[0]);
            server.Receive(buffer);
            Assert.AreEqual(3, buffer[0]);
        }

        [Test]
        public void GracefulCloseDeliversQueuedPacketsThenClosed()
        {
            LoopbackTransport.CreatePair(8, out LoopbackTransport client, out LoopbackTransport server);
            client.Send(new byte[] { 7 });
            client.CloseGracefully();

            var buffer = new byte[4];
            Assert.AreEqual(TransportSignal.Ok, server.Receive(buffer).Signal);
            Assert.AreEqual(TransportSignal.Closed, server.Receive(buffer).Signal);
            Assert.AreEqual(TransportSignal.Closed, server.Send(new byte[] { 1 }).Signal);
        }

        [Test]
        public void DisposeWithoutGracefulCloseIsAnErrorOnTheOtherEnd()
        {
            LoopbackTransport.CreatePair(8, out LoopbackTransport client, out LoopbackTransport server);
            client.Dispose();

            Assert.AreEqual(TransportSignal.Error, server.Receive(new byte[4]).Signal);
            Assert.AreEqual(TransportSignal.Error, server.Send(new byte[] { 1 }).Signal);
        }
    }
}
