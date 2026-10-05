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
    public sealed class ClientReconnectorTest
    {
        private const uint GreetingId = 0x2000_0001;
        private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);

        private static Schema TestSchema() =>
            new Schema(0xCAFE, new[] { new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }) });

        private sealed class FailingConnector : ITransportConnector
        {
            public ConnectStatus Poll(out ITransport transport)
            {
                transport = null;
                return ConnectStatus.Failed;
            }

            public void Dispose()
            {
            }
        }

        private sealed class LoopbackConnector : ITransportConnector
        {
            private readonly LoopbackListener listener;

            public LoopbackConnector(LoopbackListener listener)
            {
                this.listener = listener;
            }

            public ConnectStatus Poll(out ITransport transport)
            {
                transport = listener.Connect();
                return ConnectStatus.Connected;
            }

            public void Dispose()
            {
            }
        }

        private sealed class Harness
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly LoopbackListener SilentListener = new LoopbackListener(64);
            public readonly ServerSession Server;
            public readonly ClientSession Session;
            public readonly ClientReconnector Client;
            public readonly List<ConnectionStateArgs> States = new List<ConnectionStateArgs>();
            public readonly List<ulong> StartedPeers = new List<ulong>();
            public TimeSpan Now = TimeSpan.Zero;
            public int ConnectorsCreated;

            public Harness(ReconnectPolicy policy, Schema clientSchema = null, MessageDispatcher clientDispatcher = null)
            {
                Server = new ServerSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
                Server.OnRemoteConnectionState += args =>
                {
                    if (args.State == ConnectionState.Started)
                    {
                        StartedPeers.Add(args.PeerId);
                    }
                };
                Server.Start(Listener);
                Session = new ClientSession(clientSchema ?? TestSchema(), new SessionConfig(), new SessionLimits(), clientDispatcher ?? new MessageDispatcher(TestSchema()), TestBundles.Protocol());
                Client = new ClientReconnector(Session, policy);
                Client.OnClientConnectionState += States.Add;
            }

            public ITransportConnector Failing()
            {
                ConnectorsCreated++;
                return new FailingConnector();
            }

            public ITransportConnector Loopback()
            {
                ConnectorsCreated++;
                return new LoopbackConnector(Listener);
            }

            public ITransportConnector Silent()
            {
                ConnectorsCreated++;
                return new LoopbackConnector(SilentListener);
            }

            public void Run(int steps)
            {
                for (int step = 0; step < steps; step++)
                {
                    Now += Step;
                    Server.Tick(Now);
                    Client.Tick(Now);
                    Server.Flush();
                    Session.Flush();
                }
            }

            public List<ConnectionStateArgs> Stops() => States.FindAll(args => args.State == ConnectionState.Stopped);
        }

        [Test]
        public void FailedConnectIsRetriedAfterTheInterval()
        {
            var host = new Harness(new ReconnectPolicy { Interval = TimeSpan.FromMilliseconds(500) });
            host.Client.Start(host.Failing, host.Now);

            host.Run(1);
            Assert.AreEqual(StopReason.TransportError, host.Stops()[0].Reason);
            Assert.IsTrue(host.Stops()[0].WillRetry);

            host.Run(4);
            Assert.AreEqual(1, host.ConnectorsCreated);

            host.Run(1);
            Assert.AreEqual(2, host.ConnectorsCreated);
            Assert.AreEqual(new[] { ConnectionState.Starting, ConnectionState.Stopped, ConnectionState.Starting, ConnectionState.Stopped },
                host.States.ConvertAll(args => args.State).ToArray());
        }

        [Test]
        public void RetriesEndAfterMaxRetriesConsecutiveFailures()
        {
            var host = new Harness(new ReconnectPolicy { MaxRetries = 2, Interval = Step });
            host.Client.Start(host.Failing, host.Now);

            host.Run(20);

            Assert.AreEqual(3, host.ConnectorsCreated);
            Assert.AreEqual(new[] { true, true, false }, host.Stops().ConvertAll(args => args.WillRetry).ToArray());
        }

        [Test]
        public void PeerClosedIsRetriedAndReachingStartedResetsTheCount()
        {
            var host = new Harness(new ReconnectPolicy { MaxRetries = 1, Interval = Step });
            var plan = new Queue<Func<ITransportConnector>>(new Func<ITransportConnector>[] { host.Failing, host.Loopback, host.Loopback });
            host.Client.Start(() => plan.Dequeue()(), host.Now);

            host.Run(20);
            Assert.AreEqual(ConnectionState.Started, host.Session.State);

            host.Server.Disconnect(host.StartedPeers[0]);
            host.Run(20);

            List<ConnectionStateArgs> stops = host.Stops();
            Assert.AreEqual(StopReason.TransportError, stops[0].Reason);
            Assert.AreEqual(StopReason.PeerClosed, stops[1].Reason);
            Assert.IsTrue(stops[1].WillRetry);
            Assert.AreEqual(ConnectionState.Started, host.Session.State);
            Assert.AreEqual(2, host.StartedPeers.Count);
        }

        [Test]
        public void HandshakeFailureIsNotRetried()
        {
            var conflicting = new Schema(0xBEEF, new[] { new MessageSchema(GreetingId, 0xDEAD, new ulong[] { 0xDEAD }) });
            var host = new Harness(new ReconnectPolicy { Interval = Step }, conflicting);
            host.Client.Start(host.Loopback, host.Now);

            host.Run(20);

            Assert.AreEqual(1, host.ConnectorsCreated);
            Assert.AreEqual(StopReason.HandshakeFailed, host.Stops()[0].Reason);
            Assert.IsFalse(host.Stops()[0].WillRetry);
        }

        [Test]
        public void HandshakeTimeoutIsRetried()
        {
            var host = new Harness(new ReconnectPolicy { Interval = Step });
            host.Client.Start(host.Silent, host.Now);

            host.Run(60);

            Assert.AreEqual(StopReason.HandshakeFailed, host.Stops()[0].Reason);
            Assert.AreEqual(HandshakeFailure.Timeout, host.Stops()[0].Failure);
            Assert.IsTrue(host.Stops()[0].WillRetry);
            Assert.AreEqual(2, host.ConnectorsCreated);
        }

        [Test]
        public void HandlerExceptionIsNotRetried()
        {
            var dispatcher = new MessageDispatcher(TestSchema());
            dispatcher.Register(GreetingId, (peerId, payload) => throw new InvalidOperationException("handler failed"));
            var host = new Harness(new ReconnectPolicy { Interval = Step }, clientDispatcher: dispatcher);
            host.Client.Start(host.Loopback, host.Now);
            host.Run(20);

            host.Server.Send(host.StartedPeers[0], GreetingId, new byte[] { 1 });
            host.Run(20);

            Assert.AreEqual(1, host.ConnectorsCreated);
            Assert.AreEqual(StopReason.HandlerException, host.Stops()[0].Reason);
            Assert.IsFalse(host.Stops()[0].WillRetry);
            Assert.AreEqual(ConnectionState.Stopped, host.Session.State);
        }

        [Test]
        public void StopInTheStoppedHandlerCancelsThePendingRetry()
        {
            var host = new Harness(new ReconnectPolicy { Interval = Step });
            host.Client.OnClientConnectionState += args =>
            {
                if (args.WillRetry)
                {
                    host.Client.Stop();
                }
            };
            host.Client.Start(host.Failing, host.Now);

            host.Run(20);

            Assert.AreEqual(1, host.ConnectorsCreated);
            Assert.AreEqual(1, host.Stops().Count);
        }

        [Test]
        public void LocalStopIsNotRetried()
        {
            var host = new Harness(new ReconnectPolicy { Interval = Step });
            host.Client.Start(host.Loopback, host.Now);
            host.Run(20);

            host.Client.Stop();
            host.Run(20);

            Assert.AreEqual(1, host.ConnectorsCreated);
            Assert.AreEqual(StopReason.LocalStop, host.Stops()[0].Reason);
            Assert.IsFalse(host.Stops()[0].WillRetry);
        }

        [Test]
        public void ClientStartedWithoutRetryIsNotRetried()
        {
            var host = new Harness(new ReconnectPolicy { Interval = Step });
            host.Client.StartWithoutRetry(host.Listener.Connect(), host.Now);
            host.Run(20);
            Assert.AreEqual(ConnectionState.Started, host.Session.State);

            host.Server.Disconnect(host.StartedPeers[0]);
            host.Run(20);

            Assert.AreEqual(1, host.Stops().Count);
            Assert.IsFalse(host.Stops()[0].WillRetry);
            Assert.AreEqual(ConnectionState.Stopped, host.Session.State);
        }
    }
}
