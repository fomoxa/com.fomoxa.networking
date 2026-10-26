using System;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class CompositeListenerTest
    {
        private sealed class ScriptedListener : IListenerTransport
        {
            private readonly AcceptOutcome outcome;

            public ScriptedListener(AcceptOutcome outcome)
            {
                this.outcome = outcome;
            }

            public int AcceptCalls { get; private set; }

            public AcceptOutcome Accept()
            {
                AcceptCalls++;
                return outcome;
            }

            public void Dispose()
            {
            }
        }

        private sealed class DataTransport : ITransport, IConnectionData
        {
            public DataTransport(byte[] data)
            {
                ConnectionData = data;
            }

            public ReadOnlyMemory<byte> ConnectionData { get; }

            public TransportKind Kind => TransportKind.Message;

            public SendOutcome Send(ReadOnlySpan<byte> bytes) => SendOutcome.Ok;

            public ReceiveOutcome Receive(Span<byte> buffer) => ReceiveOutcome.WouldBlock;

            public void CloseGracefully()
            {
            }

            public void Dispose()
            {
            }
        }

        [Test]
        public void EachAcceptedTransportLeavesItsConnectionDataInAcceptOrder()
        {
            var withData = new ScriptedListener(AcceptOutcome.Accepted(new DataTransport(new byte[] { 7 })));
            var loopback = new LoopbackListener(8);
            var composite = new CompositeListener(withData, loopback);
            loopback.Connect();

            composite.Accept();
            composite.Accept();

            Assert.IsTrue(composite.TryTakeAcceptedConnectionData(out ReadOnlyMemory<byte> first));
            Assert.IsTrue(composite.TryTakeAcceptedConnectionData(out ReadOnlyMemory<byte> second));
            Assert.IsFalse(composite.TryTakeAcceptedConnectionData(out _));
            CollectionAssert.AreEqual(new byte[] { 7 }, first.ToArray());
            Assert.IsTrue(second.IsEmpty);
        }

        [Test]
        public void AnOuterListenerTakesTheConnectionDataOfAnInnerOne()
        {
            var inner = new CompositeListener(new ScriptedListener(AcceptOutcome.Accepted(new DataTransport(new byte[] { 7 }))));
            var outer = new CompositeListener(inner);

            outer.Accept();

            Assert.IsFalse(inner.TryTakeAcceptedConnectionData(out _));
            Assert.IsTrue(outer.TryTakeAcceptedConnectionData(out ReadOnlyMemory<byte> data));
            CollectionAssert.AreEqual(new byte[] { 7 }, data.ToArray());
        }

        [Test]
        public void AcceptsFromEveryListener()
        {
            var first = new LoopbackListener(8);
            var second = new LoopbackListener(8);
            var composite = new CompositeListener(first, second);
            first.Connect();
            second.Connect();

            Assert.AreEqual(AcceptStatus.Accepted, composite.Accept().Status);
            Assert.AreEqual(AcceptStatus.Accepted, composite.Accept().Status);
            Assert.AreEqual(AcceptStatus.Pending, composite.Accept().Status);
        }

        [Test]
        public void BusyListenerDoesNotStarveTheOthers()
        {
            var busy = new ScriptedListener(AcceptOutcome.Progress);
            var loopback = new LoopbackListener(8);
            var composite = new CompositeListener(busy, loopback);
            loopback.Connect();

            Assert.AreEqual(AcceptStatus.Progress, composite.Accept().Status);
            Assert.AreEqual(AcceptStatus.Accepted, composite.Accept().Status);
        }

        [Test]
        public void ErrorIsReportedOnlyWhenEveryListenerFails()
        {
            var failing = new ScriptedListener(AcceptOutcome.Error);
            var idle = new LoopbackListener(8);

            Assert.AreEqual(AcceptStatus.Pending, new CompositeListener(failing, idle).Accept().Status);
            Assert.AreEqual(AcceptStatus.Error, new CompositeListener(failing).Accept().Status);
        }
    }
}
