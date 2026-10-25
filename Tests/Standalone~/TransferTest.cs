using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Standalone.Tests
{
    public sealed class TransferTest
    {
        private const uint PlayerPrefab = 0x0000_0701;
        private const uint Fingerprint = 0x0000_F1D0;
        private const ushort PortA = 7777;
        private const ushort PortB = 7778;
        private const string TargetB = "arena-b";
        private const double FrameSeconds = 1.0 / 30;
        private static readonly byte[] SharedKey = Encoding.UTF8.GetBytes("shared-key-of-the-cluster");

        private FomoxaRegistry registry;
        private PortedLoopbackFactory factory;
        private NetworkLog log;
        private TimeSpan now;
        private GameServer serverA;
        private GameServer serverB;
        private NetworkRuntime client;
        private List<string> clientEvents;
        private List<byte[]> offeredTickets;
        private List<Exception> logged;

        [SetUp]
        public void SetUp()
        {
            registry = TestObjects.Registry(new MessageSchema(TicketCodec.Instance.MessageId, TicketCodec.Instance.MessageId, new ulong[] { TicketCodec.Instance.MessageId }));
            registry.Channels.Set(TicketCodec.Instance.MessageId, Channel.ReliableOrdered);
            factory = new PortedLoopbackFactory();
            clientEvents = new List<string>();
            offeredTickets = new List<byte[]>();
            logged = new List<Exception>();
            log = new NetworkLog(logged.Add, message => { });
            now = TimeSpan.Zero;
        }

        [TearDown]
        public void TearDown()
        {
            Assert.IsEmpty(logged);
        }

        [Test]
        public void TransferMovesTheClientToTheTargetServerWithTheTicketData()
        {
            StartAll();
            Run(10);
            ulong peerOnA = serverA.Started.Single();

            Assert.IsTrue(serverA.Transfer(peerOnA, PortB, score: 42, target: TargetB));
            Run(15);

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            Assert.AreEqual(42, serverB.ScoreOf(serverB.Started.Single()));
            Assert.AreEqual(1, client.ClientManager.Spawned.Count);
            CollectionAssert.IsEmpty(serverA.Started);
            Assert.AreEqual(StopReason.Transferred, serverA.Ended[peerOnA]);
        }

        [Test]
        public void TheClientSeesTheTransferBeforeItsObjectsGoAndTheStopAfter()
        {
            StartAll();
            Run(10);
            clientEvents.Clear();

            serverA.Transfer(serverA.Started.Single(), PortB, score: 1, target: TargetB);
            Run(15);

            CollectionAssert.AreEqual(new[]
            {
                "Transferring 127.0.0.1:7778 spawned=1",
                "Stopped Transferred retry=True spawned=0",
                "Starting None retry=False spawned=0",
                "Authenticating None retry=False spawned=0",
                "Started None retry=False spawned=0",
            }, clientEvents);
        }

        [Test]
        public void TheTicketIsOfferedOnEveryAttemptOfTheTransferAndNotAfterwards()
        {
            StartAll(startServerB: false);
            Run(10);

            serverA.Transfer(serverA.Started.Single(), PortB, score: 7, target: TargetB);
            Run(60);
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
            serverB.Start();
            Run(30);
            Assert.AreEqual(7, serverB.ScoreOf(serverB.Started.Single()));
            serverB.Runtime.ServerManager.StopConnection();
            serverB.Start();
            Run(60);

            Assert.AreEqual(0, offeredTickets[0].Length);
            int attemptsWithTicket = offeredTickets.Skip(1).TakeWhile(ticket => ticket.Length > 0).Count();
            Assert.AreEqual(1, attemptsWithTicket);
            CollectionAssert.IsEmpty(offeredTickets.Last());
            Assert.AreEqual("Stopped ServerClosed retry=False spawned=0 close=AuthenticationFailed", clientEvents.Last());
        }

        [Test]
        public void AttemptsAfterATransferGoToTheTargetAndNeverBackToTheOldServer()
        {
            StartAll(startServerB: false);
            Run(10);
            factory.Connected.Clear();

            serverA.Transfer(serverA.Started.Single(), PortB, score: 1, target: TargetB);
            Run(200);

            Assert.Greater(factory.Connected.Count, 1);
            CollectionAssert.AreEqual(Enumerable.Repeat(PortB, factory.Connected.Count), factory.Connected);
            CollectionAssert.IsEmpty(serverA.Started);
        }

        [Test]
        public void ATicketForAnotherServerIsRejectedWithoutRetry()
        {
            StartAll();
            Run(10);

            serverA.Transfer(serverA.Started.Single(), PortB, score: 1, target: "arena-c");
            Run(30);

            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
            Assert.AreEqual("Stopped ServerClosed retry=False spawned=0 close=AuthenticationFailed", clientEvents.Last());
            CollectionAssert.IsEmpty(serverB.Started);
        }

        [Test]
        public void ARejectedTicketIsNotOfferedToTheNextConnection()
        {
            StartAll();
            Run(10);
            serverA.Transfer(serverA.Started.Single(), PortB, score: 1, target: "arena-c");
            Run(30);
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);

            client.ClientManager.StartConnection("127.0.0.1", PortB);
            Run(30);

            Assert.Greater(offeredTickets[1].Length, 0);
            CollectionAssert.IsEmpty(offeredTickets[2]);
        }

        [Test]
        public void StoppingWhenTheTransferIsAnnouncedCancelsIt()
        {
            StartAll();
            Run(10);
            ulong peerOnA = serverA.Started.Single();
            client.ClientManager.OnTransferring += args => client.ClientManager.StopConnection();
            factory.Connected.Clear();
            clientEvents.Clear();

            serverA.Transfer(peerOnA, PortB, score: 1, target: TargetB);
            Run(30);

            CollectionAssert.IsEmpty(factory.Connected);
            Assert.AreEqual("Stopped LocalStop retry=False spawned=0", clientEvents.Last());
            Assert.AreEqual(StopReason.Transferred, serverA.Ended[peerOnA]);
        }

        [Test]
        public void StoppingInTheStoppedHandlerCancelsTheTransfer()
        {
            StartAll();
            Run(10);
            client.ClientManager.OnClientConnectionState += args =>
            {
                if (args.Reason == StopReason.Transferred)
                {
                    client.ClientManager.StopConnection();
                }
            };
            factory.Connected.Clear();

            serverA.Transfer(serverA.Started.Single(), PortB, score: 1, target: TargetB);
            Run(30);

            CollectionAssert.IsEmpty(factory.Connected);
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
            CollectionAssert.IsEmpty(serverB.Started);
        }

        [Test]
        public void TheLocalClientOfAHostCannotBeTransferred()
        {
            StartAll();
            Run(10);
            serverA.Runtime.ClientManager.StartConnection(null, 0);
            Run(10);
            ulong localPeer = serverA.Started.Last();

            Assert.Throws<InvalidOperationException>(() => serverA.Transfer(localPeer, PortB, score: 1, target: TargetB));
            Run(10);
            Assert.AreEqual(ConnectionState.Started, serverA.Runtime.ClientManager.State);
            Assert.AreEqual(ConnectionState.Started, serverA.Runtime.ServerManager.PeerState(localPeer));
        }

        [Test]
        public void APeerTransferBeforeAuthenticationEndsTheClient()
        {
            StartAll();
            serverB.SendTransferWhileAuthenticating = true;
            Run(10);

            serverA.Transfer(serverA.Started.Single(), PortB, score: 1, target: TargetB);
            Run(30);

            Assert.AreEqual("Stopped UnauthenticatedMessage retry=False spawned=0", clientEvents.Last());
            Assert.AreEqual(1, clientEvents.Count(entry => entry.StartsWith("Transferring")));
        }

        [Test]
        public void ObjectsAreGoneBeforeTheStoppedHandlerWhenTheClientStops()
        {
            StartAll();
            Run(10);
            clientEvents.Clear();

            client.ClientManager.StopConnection();

            CollectionAssert.AreEqual(new[] { "Stopped LocalStop retry=False spawned=0" }, clientEvents);
        }

        [Test]
        public void ObjectsAreGoneBeforeTheStoppedHandlerWhenTheServerStops()
        {
            StartAll();
            Run(10);
            clientEvents.Clear();

            serverA.Runtime.ServerManager.StopConnection();
            Run(5);

            Assert.AreEqual("Stopped PeerClosed retry=True spawned=0", clientEvents.First());
        }

        private void StartAll(bool startServerB = true)
        {
            serverA = new GameServer(this, PortA, requiresTicket: false);
            serverB = new GameServer(this, PortB, requiresTicket: true);
            serverA.Start();
            if (startServerB)
            {
                serverB.Start();
            }

            var clientPrefabs = new StandalonePrefabs();
            clientPrefabs.Register(PlayerPrefab, Fingerprint, () => new StandaloneEntity(PlayerPrefab, Array.Empty<EntityBehaviour>()));
            client = StandaloneRuntime.Create(registry, new NetworkSettings(), factory, clientPrefabs, new StandaloneBehaviours(), new StandaloneBackendTest.MemorySceneFiles(), () => now, log);
            client.ClientManager.Authenticator = new TicketClientAuthenticator(offeredTickets);
            client.ClientManager.OnTransferring += args =>
                clientEvents.Add($"Transferring {args.Address}:{args.Port} spawned={client.ClientManager.Spawned.Count}");
            client.ClientManager.OnClientConnectionState += args =>
                clientEvents.Add($"{args.State} {args.Reason} retry={args.WillRetry} spawned={client.ClientManager.Spawned.Count}"
                    + (args.CloseReason == SessionCloseReason.Unspecified ? "" : $" close={args.CloseReason}"));
            client.ClientManager.StartConnection("127.0.0.1", PortA);
        }

        private void Run(int frames)
        {
            for (int index = 0; index < frames; index++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                serverA.Runtime.BeginFrame(now, FrameSeconds);
                serverB.Runtime.BeginFrame(now, FrameSeconds);
                client.BeginFrame(now, FrameSeconds);
                serverA.Runtime.EndFrame();
                serverB.Runtime.EndFrame();
                client.EndFrame();
            }
        }

        private sealed class GameServer
        {
            private readonly TransferTest test;
            private readonly ushort port;
            private readonly TransferTickets tickets;
            private readonly Dictionary<ulong, int> scores = new Dictionary<ulong, int>();

            public GameServer(TransferTest test, ushort port, bool requiresTicket)
            {
                this.test = test;
                this.port = port;
                tickets = new TransferTickets(test.registry, SharedKey, new MemoryUsedTicketStore(), () => DateTime.UnixEpoch + test.now);
                var prefabs = new StandalonePrefabs();
                prefabs.Register(PlayerPrefab, Fingerprint, () => new StandaloneEntity(PlayerPrefab, Array.Empty<EntityBehaviour>()));
                Runtime = StandaloneRuntime.Create(test.registry, new NetworkSettings(), test.factory, prefabs, new StandaloneBehaviours(), new StandaloneBackendTest.MemorySceneFiles(), () => test.now, test.log);
                if (requiresTicket)
                {
                    Runtime.ServerManager.Authenticator = new TicketServerAuthenticator(this);
                }

                Runtime.ServerManager.OnRemoteConnectionState += OnPeerState;
            }

            public NetworkRuntime Runtime { get; }

            public List<ulong> Started { get; } = new List<ulong>();

            public Dictionary<ulong, StopReason> Ended { get; } = new Dictionary<ulong, StopReason>();

            public bool SendTransferWhileAuthenticating { get; set; }

            public void Start() => Runtime.ServerManager.StartConnection(port);

            public int ScoreOf(ulong peerId) => scores[peerId];

            public bool Transfer(ulong peerId, ushort targetPort, int score, string target)
            {
                var data = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(data, score);
                return Runtime.ServerManager.Transfer(peerId, "127.0.0.1", targetPort, tickets.Issue(target, data));
            }

            private void OnPeerState(ConnectionStateArgs args)
            {
                if (args.State == ConnectionState.Started)
                {
                    Started.Add(args.PeerId);
                    Runtime.ServerManager.Spawn(new StandaloneEntity(PlayerPrefab, Array.Empty<EntityBehaviour>()), args.PeerId);
                }
                else if (args.State == ConnectionState.Stopped)
                {
                    Started.Remove(args.PeerId);
                    Ended[args.PeerId] = args.Reason;
                }
            }

            private sealed class TicketServerAuthenticator : IServerAuthenticator
            {
                private readonly GameServer server;

                public TicketServerAuthenticator(GameServer server)
                {
                    this.server = server;
                }

                public void RegisterServer(ServerAuthentication authentication)
                {
                    authentication.On(TicketCodec.Instance, (context, ticket) =>
                    {
                        if (server.tickets.TryVerify(ticket.Bytes, TargetB, out ReadOnlyMemory<byte> data))
                        {
                            server.scores[context.PeerId] = BinaryPrimitives.ReadInt32LittleEndian(data.Span);
                            context.Accept();
                            return;
                        }

                        context.Reject();
                    });
                }

                public void OnServerAuthenticating(ServerAuthenticationContext context)
                {
                    if (server.SendTransferWhileAuthenticating)
                    {
                        context.Send(PeerTransferNetAdapter.Instance, new PeerTransfer { Address = "127.0.0.1", Port = PortA, Ticket = Array.Empty<byte>() });
                    }
                }
            }
        }

        private sealed class TicketClientAuthenticator : IClientAuthenticator
        {
            private readonly List<byte[]> offered;

            public TicketClientAuthenticator(List<byte[]> offered)
            {
                this.offered = offered;
            }

            public void RegisterClient(ClientAuthentication client)
            {
            }

            public void OnClientAuthenticating(ClientAuthenticationContext context)
            {
                offered.Add(context.Ticket.ToArray());
                context.Send(TicketCodec.Instance, new TicketMessage { Bytes = context.Ticket.ToArray() });
            }
        }

        private sealed class TicketMessage
        {
            public byte[] Bytes { get; set; }
        }

        private sealed class TicketCodec : IMessageCodec<TicketMessage>
        {
            public static readonly TicketCodec Instance = new TicketCodec();

            public uint MessageId => 0x2000_0031;

            public ReadOnlyMemory<byte> Encode(TicketMessage value) => value.Bytes;

            public void Decode(ReadOnlyMemory<byte> payload, ref TicketMessage value)
            {
                value ??= new TicketMessage();
                value.Bytes = payload.ToArray();
            }
        }

        private sealed class PortedLoopbackFactory : ITransportFactory
        {
            private readonly Dictionary<ushort, LoopbackListener> listeners = new Dictionary<ushort, LoopbackListener>();

            public List<ushort> Connected { get; } = new List<ushort>();

            public int FrameBudget => FomoxaWire.MaxDataFrameSize;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                boundPort = port;
                var listener = new LoopbackListener(256);
                listeners[port] = listener;
                return new OwnedListener(listener, () => listeners.Remove(port));
            }

            public ITransportConnector CreateConnector(string address, ushort port)
            {
                Connected.Add(port);
                return new Connector(listeners, port);
            }

            private sealed class OwnedListener : IListenerTransport
            {
                private readonly LoopbackListener inner;
                private readonly Action released;

                public OwnedListener(LoopbackListener inner, Action released)
                {
                    this.inner = inner;
                    this.released = released;
                }

                public AcceptOutcome Accept() => inner.Accept();

                public void Dispose()
                {
                    released();
                    inner.Dispose();
                }
            }

            private sealed class Connector : ITransportConnector
            {
                private readonly Dictionary<ushort, LoopbackListener> listeners;
                private readonly ushort port;

                public Connector(Dictionary<ushort, LoopbackListener> listeners, ushort port)
                {
                    this.listeners = listeners;
                    this.port = port;
                }

                public ConnectStatus Poll(out ITransport transport)
                {
                    transport = null;
                    if (!listeners.TryGetValue(port, out LoopbackListener listener))
                    {
                        return ConnectStatus.Failed;
                    }

                    transport = listener.Connect();
                    return ConnectStatus.Connected;
                }

                public void Dispose()
                {
                }
            }
        }
    }
}
