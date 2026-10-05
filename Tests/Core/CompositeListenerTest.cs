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
