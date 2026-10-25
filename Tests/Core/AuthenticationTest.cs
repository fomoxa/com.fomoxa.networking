using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class AuthenticationTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const uint CredentialId = 0x2000_0003;
        private const uint VerdictId = 0x2000_0004;
        private const int UdpBudget = 1200;

        private static Schema HostSchema() =>
            new Schema(0xCAFE, new[]
            {
                new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }),
                new MessageSchema(CredentialId, 0xF00E, new ulong[] { 0xF00E }),
                new MessageSchema(VerdictId, 0xF00F, new ulong[] { 0xF00F }),
                new MessageSchema(TestBundles.LocalPeerId, 0xF010, new ulong[] { 0xF010 }),
            });

        private static MessageChannels ReliableChannels()
        {
            var channels = new MessageChannels();
            channels.Set(GreetingId, Channel.ReliableOrdered);
            channels.Set(CredentialId, Channel.ReliableOrdered);
            channels.Set(VerdictId, Channel.ReliableOrdered);
            return channels;
        }

        private sealed class RecordingTransport : ITransport
        {
            private readonly ITransport inner;

            public RecordingTransport(ITransport inner)
            {
                this.inner = inner;
            }

            public List<uint> SentEntries { get; } = new List<uint>();

            public TransportKind Kind => inner.Kind;

            public SendOutcome Send(ReadOnlySpan<byte> bytes)
            {
                if (bytes[0] == 0 && BitConverter.ToUInt32(bytes.Slice(3, 4)) == TestBundles.MessageId)
                {
                    var bundle = new MessageBundle();
                    MessageBundleNetAdapter.Instance.Decode(bytes.Slice(FomoxaWire.DataFrameHeaderSize).ToArray(), ref bundle);
                    SentEntries.AddRange(bundle.Entries.Select(entry => entry.MessageId));
                }

                return inner.Send(bytes);
            }

            public ReceiveOutcome Receive(Span<byte> buffer) => inner.Receive(buffer);

            public void CloseGracefully() => inner.CloseGracefully();

            public void Dispose() => inner.Dispose();
        }

        private sealed class Host
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly List<(ConnectionStateArgs Args, TimeSpan At)> PeerStates = new List<(ConnectionStateArgs, TimeSpan)>();
            public readonly List<ConnectionStateArgs> ClientStates = new List<ConnectionStateArgs>();
            public readonly List<ulong> AuthenticatingPeers = new List<ulong>();
            public readonly Dictionary<ulong, ServerAuthenticationContext> Contexts = new Dictionary<ulong, ServerAuthenticationContext>();
            public readonly List<(ulong PeerId, byte[] Payload)> Credentials = new List<(ulong, byte[])>();
            public readonly List<byte[]> ServerGreetings = new List<byte[]>();
            public readonly List<string> ClientLog = new List<string>();
            public readonly List<uint> RawReceived = new List<uint>();
            public readonly List<HandlerExceptionArgs> ServerExceptions = new List<HandlerExceptionArgs>();
            public readonly List<HandlerExceptionArgs> ClientExceptions = new List<HandlerExceptionArgs>();
            public readonly ServerSession Server;
            public Action<ServerAuthenticationContext> OnAuthenticating;
            public Action<ServerAuthenticationContext, byte[]> OnCredential;
            public Action<ulong, byte[]> OnGreeting;
            public Action<ClientAuthenticationContext> OnClientAuthenticating;
            public Action OnVerdict;
            public TimeSpan Now = TimeSpan.Zero;

            public Host(bool authenticates = true, SessionLimits limits = null, MessageChannels channels = null, int frameBudget = FomoxaWire.MaxDataFrameSize, bool greetsBeforeLocalPeer = false)
            {
                var dispatcher = new MessageDispatcher(HostSchema());
                dispatcher.Register(GreetingId, (peerId, payload) =>
                {
                    ServerGreetings.Add(payload.ToArray());
                    OnGreeting?.Invoke(peerId, payload.ToArray());
                });
                dispatcher.RegisterAuthentication(CredentialId, (peerId, payload) =>
                {
                    Credentials.Add((peerId, payload.ToArray()));
                    OnCredential?.Invoke(Server.AuthenticationContextOf(peerId), payload.ToArray());
                });
                Server = new ServerSession(HostSchema(), new SessionConfig(), limits ?? new SessionLimits(), dispatcher, TestBundles.Protocol(channels ?? ReliableChannels(), frameBudget));
                if (greetsBeforeLocalPeer)
                {
                    Server.SetPeerStartHandler((ulong peerId, out uint failedMessageId, out Exception exception) =>
                    {
                        failedMessageId = 0;
                        exception = null;
                        Server.EnqueueObjectMessage(peerId, GreetingId, new byte[] { 0 });
                        Server.EnqueueObjectMessage(peerId, TestBundles.LocalPeerId, ReadOnlySpan<byte>.Empty);
                        return true;
                    });
                }
                else
                {
                    Server.SendingLocalPeer();
                }

                if (authenticates)
                {
                    Server.SetAuthenticatingHandler(context =>
                    {
                        AuthenticatingPeers.Add(context.PeerId);
                        Contexts[context.PeerId] = context;
                        OnAuthenticating?.Invoke(context);
                    });
                }

                Server.OnRemoteConnectionState += args => PeerStates.Add((args, Now));
                Server.OnHandlerException += ServerExceptions.Add;
                Server.Start(Listener);
            }

            public ClientSession Client { get; private set; }

            public FomoxaConnection Raw { get; private set; }

            public ulong PeerId => PeerStates[0].Args.PeerId;

            public List<ClientAuthenticationContext> ClientContexts { get; } = new List<ClientAuthenticationContext>();

            public ServerAuthenticationContext Context => Contexts[PeerId];

            public ClientAuthenticationContext ClientContext => ClientContexts[ClientContexts.Count - 1];

            public ConnectionState PeerState => Server.PeerState(PeerId);

            public ConnectionStateArgs PeerStop => PeerStates.Single(state => state.Args.State == ConnectionState.Stopped).Args;

            public TimeSpan PeerTime(ConnectionState state) => PeerStates.Single(entry => entry.Args.State == state).At;

            public ConnectionStateArgs ClientStop => ClientStates.Single(args => args.State == ConnectionState.Stopped);

            public RecordingTransport ClientTransport { get; private set; }

            public ClientSession Connect(SessionLimits clientLimits = null, int frameBudget = FomoxaWire.MaxDataFrameSize)
            {
                var dispatcher = new MessageDispatcher(HostSchema());
                dispatcher.Register(GreetingId, (peerId, payload) => ClientLog.Add($"greeting:{Client.State}"));
                dispatcher.Register(TestBundles.LocalPeerId, (peerId, payload) => ClientLog.Add($"local-peer:{Client.State}"));
                dispatcher.RegisterAuthentication(VerdictId, (peerId, payload) =>
                {
                    ClientLog.Add($"verdict:{Client.State}");
                    OnVerdict?.Invoke();
                });
                Client = new ClientSession(HostSchema(), new SessionConfig(), clientLimits ?? new SessionLimits(), dispatcher, TestBundles.Protocol(ReliableChannels(), frameBudget)).AcceptingLocalPeer();
                Client.SetAuthenticatingHandler(context =>
                {
                    ClientContexts.Add(context);
                    OnClientAuthenticating?.Invoke(context);
                });
                Client.OnClientConnectionState += ClientStates.Add;
                Client.OnHandlerException += ClientExceptions.Add;
                ClientTransport = new RecordingTransport(Listener.Connect());
                Client.Start(ClientTransport, Now);
                return Client;
            }

            public void ConnectRaw()
            {
                Raw = FomoxaConnection.Connect(Listener.Connect(), HostSchema(), new SessionConfig(), Now);
            }

            public void SendRaw(params (uint MessageId, byte[] Data)[] entries)
            {
                var bundle = new MessageBundle();
                foreach ((uint messageId, byte[] data) in entries)
                {
                    bundle.Entries.Add(new BundleEntry { MessageId = messageId, Data = data });
                }

                Assert.AreEqual(SendStatus.Sent, Raw.Send(TestBundles.MessageId, MessageBundleNetAdapter.Instance.Encode(bundle).Span));
            }

            public void Step(int count)
            {
                for (int step = 0; step < count; step++)
                {
                    Now += TimeSpan.FromMilliseconds(16);
                    Server.Tick(Now);
                    Client?.Tick(Now);
                    TickRaw();
                    Server.Flush();
                    Client?.Flush();
                }
            }

            private void TickRaw()
            {
                if (Raw == null)
                {
                    return;
                }

                foreach (FomoxaEvent raised in Raw.Tick(Now))
                {
                    if (raised.Kind != FomoxaEventKind.Message || raised.MessageId != TestBundles.MessageId)
                    {
                        continue;
                    }

                    var bundle = new MessageBundle();
                    MessageBundleNetAdapter.Instance.Decode(raised.Payload, ref bundle);
                    RawReceived.AddRange(bundle.Entries.Select(entry => entry.MessageId));
                }
            }
        }

        private static MessageChannels UnreliableChannels() => new MessageChannels();

        [Test]
        public void WithoutAnAuthenticatorThePeerPassesAuthenticatingInTheTickOfReady()
        {
            var host = new Host(authenticates: false);
            host.Connect();
            host.Step(20);

            CollectionAssert.AreEqual(
                new[] { ConnectionState.Starting, ConnectionState.Authenticating, ConnectionState.Started },
                host.PeerStates.Select(state => state.Args.State));
            Assert.AreEqual(host.PeerTime(ConnectionState.Authenticating), host.PeerTime(ConnectionState.Started));
            CollectionAssert.AreEqual(
                new[] { ConnectionState.Starting, ConnectionState.Authenticating, ConnectionState.Started },
                host.ClientStates.Select(args => args.State));
        }

        [Test]
        public void WithAnAuthenticatorThePeerAndTheClientWaitInAuthenticating()
        {
            var host = new Host();
            host.Connect();
            host.Step(30);

            Assert.AreEqual(ConnectionState.Authenticating, host.PeerState);
            Assert.AreEqual(ConnectionState.Authenticating, host.Client.State);
            CollectionAssert.AreEqual(new[] { host.PeerId }, host.AuthenticatingPeers);
            CollectionAssert.IsEmpty(host.ClientLog);
        }

        [Test]
        public void AcceptStartsThePeerAndLocalPeerStartsTheClientAtThatEntry()
        {
            var host = new Host();
            host.OnAuthenticating = context => Assert.IsTrue(context.Accept());
            host.Connect();
            host.Step(20);

            Assert.AreEqual(ConnectionState.Started, host.PeerState);
            Assert.AreEqual(ConnectionState.Started, host.Client.State);
            CollectionAssert.AreEqual(new[] { "local-peer:Started" }, host.ClientLog);
        }

        [Test]
        public void AClientStoppedWhenItReachesStartedDoesNotHandleLocalPeer()
        {
            var host = new Host(authenticates: false);
            ClientSession client = host.Connect();
            client.OnClientConnectionState += args =>
            {
                if (args.State == ConnectionState.Started)
                {
                    client.Stop();
                }
            };
            host.Step(20);

            Assert.AreEqual(ConnectionState.Stopped, client.State);
            CollectionAssert.IsEmpty(host.ClientLog);
        }

        [Test]
        public void TheReplyOfTheAuthenticatorArrivesBeforeLocalPeer()
        {
            var host = new Host();
            host.OnClientAuthenticating = context => Assert.AreEqual(SendResult.Queued, host.Client.SendAuthentication(context, CredentialId, new byte[] { 7 }));
            host.OnCredential = (context, payload) =>
            {
                Assert.AreEqual(SendResult.Queued, host.Server.SendAuthentication(context, VerdictId, new byte[] { 1 }));
                Assert.IsTrue(context.Accept());
            };
            host.Connect();
            host.Step(20);

            Assert.AreEqual(1, host.Credentials.Count);
            Assert.AreEqual(host.PeerId, host.Credentials[0].PeerId);
            CollectionAssert.AreEqual(new byte[] { 7 }, host.Credentials[0].Payload);
            CollectionAssert.AreEqual(new[] { "verdict:Authenticating", "local-peer:Started" }, host.ClientLog);
        }

        [Test]
        public void TheClientDropsWhatItsAuthenticatorStillQueuedAtLocalPeer()
        {
            var host = new Host();
            host.OnClientAuthenticating = context => host.Client.SendAuthentication(context, CredentialId, new byte[] { 7 });
            host.OnCredential = (context, payload) =>
            {
                host.Server.SendAuthentication(context, VerdictId, new byte[] { 1 });
                context.Accept();
            };
            host.OnVerdict = () => Assert.AreEqual(SendResult.Queued, host.Client.SendAuthentication(host.ClientContext, CredentialId, new byte[] { 9 }));
            host.Connect();
            host.Step(20);

            Assert.AreEqual(ConnectionState.Started, host.Client.State);
            Assert.AreEqual(1, host.ClientTransport.SentEntries.Count(id => id == CredentialId));
        }

        [Test]
        public void MessagesQueuedBeforeAcceptanceStayOffTheWireUntilStarted()
        {
            var host = new Host();
            host.Connect();
            host.Step(20);

            Assert.AreEqual(SendResult.Queued, host.Client.Send(GreetingId, new byte[] { 1 }));
            Assert.AreEqual(SendResult.Queued, host.Server.Send(host.PeerId, GreetingId, new byte[] { 2 }));
            host.Step(10);

            CollectionAssert.IsEmpty(host.ServerGreetings);
            CollectionAssert.IsEmpty(host.ClientLog);
            Assert.AreEqual(ConnectionState.Authenticating, host.PeerState);
            Assert.AreEqual(ConnectionState.Authenticating, host.Client.State);

            Assert.IsTrue(host.Context.Accept());
            host.Step(10);

            Assert.AreEqual(1, host.ServerGreetings.Count);
            CollectionAssert.AreEqual(new byte[] { 1 }, host.ServerGreetings[0]);
            CollectionAssert.AreEqual(new[] { "local-peer:Started", "greeting:Started" }, host.ClientLog);
        }

        [Test]
        public void BroadcastSkipsAPeerInAuthenticating()
        {
            var host = new Host();
            host.Connect();
            host.Step(20);

            Assert.AreEqual(0, host.Server.Broadcast(GreetingId, new byte[] { 1 }));
        }

        [Test]
        public void RejectClosesThePeerAfterTheReplyAndDiscardsWhatWasHeld()
        {
            var host = new Host();
            TimeSpan rejectedAt = TimeSpan.Zero;
            host.OnClientAuthenticating = context => host.Client.SendAuthentication(context, CredentialId, new byte[] { 7 });
            host.OnCredential = (context, payload) =>
            {
                rejectedAt = host.Now;
                host.Server.Send(context.PeerId, GreetingId, new byte[] { 5 });
                host.Server.SendAuthentication(context, VerdictId, new byte[] { 0 });
                Assert.IsTrue(context.Reject());
            };
            host.Connect();
            host.Step(20);

            CollectionAssert.AreEqual(new[] { "verdict:Authenticating" }, host.ClientLog);
            Assert.AreEqual(StopReason.ServerClosed, host.ClientStop.Reason);
            Assert.AreEqual(SessionCloseReason.AuthenticationFailed, host.ClientStop.CloseReason);
            Assert.AreEqual(StopReason.AuthenticationFailed, host.PeerStop.Reason);
            Assert.AreEqual(1, host.PeerStop.DiscardedMessages);
            Assert.AreEqual(rejectedAt, host.PeerTime(ConnectionState.Stopped));
        }

        [Test]
        public void AnAuthenticationThatRunsOutEndsWithAuthenticationTimeout()
        {
            var host = new Host(limits: new SessionLimits { AuthenticationTimeout = TimeSpan.FromMilliseconds(100) });
            host.Connect();
            host.Step(30);

            TimeSpan waited = host.PeerTime(ConnectionState.Stopped) - host.PeerTime(ConnectionState.Authenticating);
            Assert.AreEqual(StopReason.AuthenticationTimeout, host.PeerStop.Reason);
            Assert.AreEqual(StopReason.ServerClosed, host.ClientStop.Reason);
            Assert.AreEqual(SessionCloseReason.AuthenticationTimeout, host.ClientStop.CloseReason);
            Assert.GreaterOrEqual(waited, TimeSpan.FromMilliseconds(100));
            Assert.Less(waited, TimeSpan.FromMilliseconds(100 + 16));
        }

        [Test]
        public void TheDefaultTimeoutIsThirtySecondsAndZeroWaitsWithoutEnd()
        {
            var bounded = new Host();
            bounded.Connect();
            var unbounded = new Host(limits: new SessionLimits { AuthenticationTimeout = TimeSpan.Zero });
            unbounded.Connect();

            bounded.Step(1870);
            unbounded.Step(2000);

            Assert.AreEqual(TimeSpan.FromSeconds(30), new SessionLimits().AuthenticationTimeout);
            Assert.AreEqual(ConnectionState.Authenticating, bounded.PeerState);
            bounded.Step(10);
            Assert.AreEqual(StopReason.AuthenticationTimeout, bounded.PeerStop.Reason);
            Assert.AreEqual(ConnectionState.Authenticating, unbounded.PeerState);
            Assert.AreEqual(ConnectionState.Authenticating, unbounded.Client.State);
        }

        [Test]
        public void APeerSendingOutsideTheGateIsDisconnectedWithoutSessionClose()
        {
            var host = new Host(channels: UnreliableChannels());
            host.ConnectRaw();
            host.Step(20);
            Assert.AreEqual(ConnectionState.Authenticating, host.PeerState);

            host.SendRaw((GreetingId, new byte[] { 1 }));
            host.Step(5);

            Assert.AreEqual(StopReason.UnauthenticatedMessage, host.PeerStop.Reason);
            CollectionAssert.IsEmpty(host.ServerGreetings);
            CollectionAssert.DoesNotContain(host.RawReceived, TestBundles.CloseId);
        }

        [Test]
        public void PeerLeaveIsOutsideTheGate()
        {
            var host = new Host(channels: UnreliableChannels());
            host.ConnectRaw();
            host.Step(20);

            host.SendRaw((TestBundles.LeaveId, PeerLeaveNetAdapter.Instance.Encode(new PeerLeave()).ToArray()));
            host.Step(5);

            Assert.AreEqual(StopReason.UnauthenticatedMessage, host.PeerStop.Reason);
        }

        [Test]
        public void AcceptInsideAnAuthenticationHandlerLetsTheRestOfTheBundleThrough()
        {
            var host = new Host(channels: UnreliableChannels());
            host.OnCredential = (context, payload) => context.Accept();
            host.ConnectRaw();
            host.Step(20);

            host.SendRaw((CredentialId, new byte[] { 1 }), (GreetingId, new byte[] { 2 }));
            host.Step(5);

            Assert.AreEqual(ConnectionState.Started, host.PeerState);
            Assert.AreEqual(1, host.ServerGreetings.Count);
        }

        [Test]
        public void RejectInsideAnAuthenticationHandlerSkipsTheRestOfTheBundle()
        {
            var host = new Host(channels: UnreliableChannels());
            host.OnCredential = (context, payload) => context.Reject();
            host.ConnectRaw();
            host.Step(20);

            host.SendRaw((CredentialId, new byte[] { 1 }), (CredentialId, new byte[] { 2 }));
            host.Step(5);

            Assert.AreEqual(1, host.Credentials.Count);
            Assert.AreEqual(StopReason.AuthenticationFailed, host.PeerStop.Reason);
        }

        [Test]
        public void KickInsideAHandlerSkipsTheRestOfTheBundle()
        {
            var host = new Host(authenticates: false, channels: UnreliableChannels());
            host.OnGreeting = (peerId, payload) => host.Server.Kick(peerId);
            host.ConnectRaw();
            host.Step(20);

            host.SendRaw((GreetingId, new byte[] { 1 }), (GreetingId, new byte[] { 2 }));
            host.Step(1);

            Assert.AreEqual(1, host.ServerGreetings.Count);
        }

        [Test]
        public void AClientReceivingOutsideTheGateEndsWithoutDeliveringIt()
        {
            var host = new Host(authenticates: false, greetsBeforeLocalPeer: true);
            host.Connect();
            host.Step(20);

            Assert.AreEqual(StopReason.UnauthenticatedMessage, host.ClientStop.Reason);
            CollectionAssert.IsEmpty(host.ClientLog);
        }

        [Test]
        public void AnAuthenticationModelAfterStartedHasNoHandler()
        {
            var host = new Host(authenticates: false);
            host.Connect();
            host.Step(20);

            Assert.AreEqual(SendResult.Queued, host.Client.Send(CredentialId, new byte[] { 3 }));
            host.Step(5);

            CollectionAssert.IsEmpty(host.Credentials);
            Assert.AreEqual(ConnectionState.Started, host.PeerState);
            Assert.AreEqual(ConnectionState.Started, host.Client.State);
        }

        [Test]
        public void AContextOfASessionThatEndedReportsIt()
        {
            var host = new Host();
            host.Connect();
            host.Step(20);
            ServerAuthenticationContext server = host.Context;
            ClientAuthenticationContext client = host.ClientContext;

            host.Server.Disconnect(host.PeerId);
            host.Client.Stop();

            Assert.IsFalse(server.Accept());
            Assert.IsFalse(server.Reject());
            Assert.AreEqual(SendResult.NotConnected, host.Server.SendAuthentication(server, VerdictId, new byte[] { 1 }));
            Assert.AreEqual(SendResult.NotConnected, host.Client.SendAuthentication(client, CredentialId, new byte[] { 1 }));
        }

        [Test]
        public void AContextOfAnAcceptedSessionThrows()
        {
            var host = new Host();
            host.OnAuthenticating = context => context.Accept();
            host.Connect();
            host.Step(20);

            Assert.AreEqual(ConnectionState.Started, host.Client.State);
            Assert.Throws<InvalidOperationException>(() => host.Context.Accept());
            Assert.Throws<InvalidOperationException>(() => host.Context.Reject());
            Assert.Throws<InvalidOperationException>(() => host.Server.SendAuthentication(host.Context, VerdictId, new byte[] { 1 }));
            Assert.Throws<InvalidOperationException>(() => host.Client.SendAuthentication(host.ClientContext, CredentialId, new byte[] { 1 }));
        }

        [Test]
        public void AContextOfAPeerBeingClosedReportsItEnded()
        {
            var host = new Host();
            host.Connect();
            host.Step(20);

            Assert.IsTrue(host.Server.Kick(host.PeerId));

            Assert.IsFalse(host.Context.Accept());
            Assert.IsFalse(host.Context.Reject());
            Assert.AreEqual(SendResult.NotConnected, host.Server.SendAuthentication(host.Context, VerdictId, new byte[] { 1 }));
            host.Step(5);
            Assert.AreEqual(StopReason.ServerClosed, host.ClientStop.Reason);
            Assert.AreEqual(SessionCloseReason.Kicked, host.ClientStop.CloseReason);
            Assert.AreEqual(StopReason.LocalStop, host.PeerStop.Reason);
        }

        [Test]
        public void AContextOfAnEarlierAttemptDoesNotReachTheNextOne()
        {
            var host = new Host();
            host.Connect();
            host.Step(20);
            ServerAuthenticationContext firstServer = host.Context;
            ClientAuthenticationContext firstClient = host.ClientContext;
            host.Client.Stop();
            host.Step(5);

            host.Client.Start(host.Listener.Connect(), host.Now);
            SendResult whileStarting = host.Client.SendAuthentication(firstClient, CredentialId, new byte[] { 1 });
            host.Step(20);

            Assert.AreEqual(SendResult.NotConnected, whileStarting);
            Assert.AreEqual(2, host.ClientContexts.Count);
            Assert.AreEqual(ConnectionState.Authenticating, host.Client.State);
            Assert.AreEqual(SendResult.NotConnected, host.Client.SendAuthentication(firstClient, CredentialId, new byte[] { 1 }));
            Assert.IsFalse(firstServer.Accept());
            Assert.AreEqual(ConnectionState.Authenticating, host.Client.State);
            CollectionAssert.IsEmpty(host.Credentials);
        }

        [Test]
        public void AContextFromBeforeAServerRestartDoesNotReachAPeerWithTheSameId()
        {
            var host = new Host();
            host.Connect();
            host.Step(20);
            ServerAuthenticationContext before = host.Context;
            host.Server.Stop();
            host.Client.Stop();
            host.Server.Start(host.Listener);
            host.Client.Start(host.Listener.Connect(), host.Now);
            host.Step(20);

            Assert.AreEqual(before.PeerId, host.AuthenticatingPeers.Last());
            Assert.AreNotSame(before, host.Context);
            Assert.IsFalse(before.Accept());
            Assert.AreEqual(ConnectionState.Authenticating, host.Server.PeerState(before.PeerId));
        }

        [Test]
        public void TheTokensAreCancelledWhenTheSessionIsAccepted()
        {
            var host = new Host();
            host.Connect();
            host.Step(20);
            CancellationToken server = host.Context.CancellationToken;
            CancellationToken client = host.ClientContext.CancellationToken;
            Assert.IsFalse(server.IsCancellationRequested);
            Assert.IsFalse(client.IsCancellationRequested);

            Assert.IsTrue(host.Context.Accept());
            Assert.IsTrue(server.IsCancellationRequested);
            Assert.IsFalse(client.IsCancellationRequested);
            host.Step(5);

            Assert.AreEqual(ConnectionState.Started, host.Client.State);
            Assert.IsTrue(client.IsCancellationRequested);
        }

        [Test]
        public void TheTokensAreCancelledWhenTheSessionEndsWithoutAcceptance()
        {
            var rejected = new Host();
            rejected.Connect();
            rejected.Step(20);
            var dropped = new Host();
            dropped.Connect();
            dropped.Step(20);
            var timedOut = new Host(limits: new SessionLimits { AuthenticationTimeout = TimeSpan.FromMilliseconds(100) });
            timedOut.Connect();
            var stopped = new Host();
            stopped.Connect();
            stopped.Step(20);

            Assert.IsTrue(rejected.Context.Reject());
            Assert.IsTrue(rejected.Context.CancellationToken.IsCancellationRequested);
            rejected.Step(5);
            dropped.Client.Stop();
            Assert.IsTrue(dropped.ClientContext.CancellationToken.IsCancellationRequested);
            dropped.Step(5);
            timedOut.Step(30);
            stopped.Server.Stop();

            Assert.IsTrue(rejected.ClientContext.CancellationToken.IsCancellationRequested);
            Assert.AreEqual(ConnectionState.Stopped, dropped.PeerStop.State);
            Assert.IsTrue(dropped.Context.CancellationToken.IsCancellationRequested);
            Assert.IsTrue(timedOut.Context.CancellationToken.IsCancellationRequested);
            Assert.IsTrue(stopped.Context.CancellationToken.IsCancellationRequested);
        }

        [Test]
        public void ACancellationCallbackThatThrowsAtAcceptanceEndsTheSessionWithHandlerException()
        {
            var host = new Host();
            host.OnAuthenticating = context => context.CancellationToken.Register(() => throw new InvalidOperationException("server callback failed"));
            host.OnClientAuthenticating = context => context.CancellationToken.Register(() => throw new InvalidOperationException("client callback failed"));
            host.Connect();
            host.Step(20);

            Assert.IsTrue(host.Context.Accept());
            host.Step(10);

            Assert.AreEqual(0u, host.ServerExceptions.Single().MessageId);
            Assert.AreEqual(StopReason.HandlerException, host.PeerStop.Reason);
            Assert.AreEqual(0u, host.ClientExceptions.Single().MessageId);
            Assert.AreEqual(StopReason.HandlerException, host.ClientStop.Reason);
        }

        [Test]
        public void ACancellationCallbackThatThrowsWhileTheSessionEndsIsOnlyReported()
        {
            var host = new Host();
            host.OnAuthenticating = context => context.CancellationToken.Register(() => throw new InvalidOperationException("server callback failed"));
            host.OnClientAuthenticating = context => context.CancellationToken.Register(() => throw new InvalidOperationException("client callback failed"));
            host.Connect();
            host.Step(20);

            Assert.IsTrue(host.Server.Kick(host.PeerId));
            host.Client.Stop();
            host.Step(5);

            Assert.AreEqual(host.PeerId, host.ServerExceptions.Single().PeerId);
            Assert.AreEqual(StopReason.LocalStop, host.PeerStop.Reason);
            Assert.AreEqual(1, host.ClientExceptions.Count);
            Assert.AreEqual(StopReason.LocalStop, host.ClientStop.Reason);
        }

        [Test]
        public void AnExceptionWhenThePeerEntersAuthenticatingEndsItWithHandlerException()
        {
            var host = new Host();
            host.OnAuthenticating = context => throw new InvalidOperationException("authenticator failed");
            host.Connect();
            host.Step(20);

            Assert.AreEqual(host.PeerId, host.ServerExceptions.Single().PeerId);
            Assert.AreEqual(0u, host.ServerExceptions.Single().MessageId);
            Assert.AreEqual(StopReason.HandlerException, host.PeerStop.Reason);
            Assert.AreEqual(StopReason.ServerClosed, host.ClientStop.Reason);
            Assert.AreEqual(SessionCloseReason.HandlerException, host.ClientStop.CloseReason);
        }

        [Test]
        public void AnExceptionInAnAuthenticationHandlerCarriesItsMessageId()
        {
            var host = new Host();
            host.OnClientAuthenticating = context => host.Client.SendAuthentication(context, CredentialId, new byte[] { 7 });
            host.OnCredential = (context, payload) => throw new InvalidOperationException("credential handler failed");
            host.Connect();
            host.Step(20);

            Assert.AreEqual(CredentialId, host.ServerExceptions.Single().MessageId);
            Assert.AreEqual(StopReason.HandlerException, host.PeerStop.Reason);
        }

        [Test]
        public void AnExceptionInTheClientAuthenticatorStopsTheClient()
        {
            var host = new Host();
            host.OnClientAuthenticating = context => throw new InvalidOperationException("client authenticator failed");
            host.Connect();
            host.Step(20);

            Assert.AreEqual(0u, host.ClientExceptions.Single().MessageId);
            Assert.AreEqual(StopReason.HandlerException, host.ClientStop.Reason);
        }

        [Test]
        public void AFragmentedCredentialCutByAcceptanceDoesNotBreakLaterFragments()
        {
            var host = new Host(frameBudget: UdpBudget);
            host.OnClientAuthenticating = context => Assert.AreEqual(SendResult.Queued, host.Client.SendAuthentication(context, CredentialId, new byte[30000]));
            host.Connect(new SessionLimits { ReliableWindow = 2 }, UdpBudget);
            host.Step(6);
            CollectionAssert.IsEmpty(host.Credentials);

            Assert.IsTrue(host.Context.Accept());
            host.Step(30);
            Assert.AreEqual(SendResult.Queued, host.Client.Send(GreetingId, new byte[6000]));
            host.Step(60);

            Assert.AreEqual(6000, host.ServerGreetings.Single().Length);
            CollectionAssert.IsEmpty(host.Credentials);
            Assert.AreEqual(ConnectionState.Started, host.PeerState);
        }
    }
}
