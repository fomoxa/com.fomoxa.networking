using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BundleFixture;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Standalone.Tests
{
    public sealed class EncryptedTransportTest
    {
        private const uint PlayerPrefab = 0x0000_0701;
        private const uint Fingerprint = 0x0000_F1D0;
        private const double FrameSeconds = 1.0 / 30;
        private static readonly byte[] Key = Enumerable.Range(1, ConnectTokens.KeyLength).Select(value => (byte)value).ToArray();

        private FomoxaRegistry registry;
        private ConnectTokens tokens;
        private TimeSpan now;
        private List<Exception> exceptions;
        private List<string> errors;
        private NetworkLog log;
        private List<Server> servers;
        private NetworkRuntime client;
        private List<string> clientEvents;

        [SetUp]
        public void SetUp()
        {
            registry = TestObjects.Registry();
            now = TimeSpan.Zero;
            tokens = new ConnectTokens(registry, Key, null, () => DateTime.UnixEpoch + now);
            exceptions = new List<Exception>();
            errors = new List<string>();
            log = new NetworkLog(exceptions.Add, message => { }, errors.Add);
            servers = new List<Server>();
            clientEvents = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            client?.ClientManager.StopConnection();
            foreach (Server server in servers)
            {
                server.Runtime.ServerManager.StopConnection();
            }

            Assert.IsEmpty(exceptions);
        }

        [Test]
        public void AClientWithAGrantStartsOnAnEncryptedServerAndItsAuthenticatorSeesTheData()
        {
            Server server = StartServer("pod-a", encrypted: true);
            client = CreateClient(encrypted: true);

            client.ClientManager.StartConnection("127.0.0.1", server.Port, tokens.Issue("pod-a", new byte[] { 3, 1 }));
            Run(20);

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            Assert.AreEqual(1, client.ClientManager.Spawned.Count);
            CollectionAssert.AreEqual(new byte[] { 3, 1 }, server.SeenData.Single());
        }

        [Test]
        public void ThePlainTransportGivesTheAuthenticatorNoConnectionData()
        {
            Server server = StartServer("pod-a", encrypted: false);
            client = CreateClient(encrypted: false);

            client.ClientManager.StartConnection("127.0.0.1", server.Port);
            Run(20);

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            CollectionAssert.IsEmpty(server.SeenData.Single());
        }

        [Test]
        public void StartingWithoutAConnectionOnAnEncryptedTransportThrows()
        {
            client = CreateClient(encrypted: true);

            Assert.Throws<InvalidOperationException>(() => client.ClientManager.StartConnection("127.0.0.1", 7777));
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
        }

        [Test]
        public void TheManagerRefusesToStartWithoutAConnectionOnAnyTransportThatRequiresOne()
        {
            client = CreateClient(new RequiresConnectionFactory());

            Assert.Throws<InvalidOperationException>(() => client.ClientManager.StartConnection("127.0.0.1", 7777));
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
        }

        [Test]
        public void StartingWithAConnectionOnAPlainTransportOrAnUnreadableConnectionThrows()
        {
            NetworkRuntime plain = CreateClient(encrypted: false);
            client = CreateClient(encrypted: true);

            Assert.Throws<ArgumentException>(() => plain.ClientManager.StartConnection("127.0.0.1", 7777, tokens.Issue("pod-a", ReadOnlyMemory<byte>.Empty)));
            Assert.Throws<ArgumentException>(() => client.ClientManager.StartConnection("127.0.0.1", 7777, new byte[] { 1, 2, 3 }));
            Assert.Throws<ArgumentException>(() => client.ClientManager.StartConnection("127.0.0.1", 7777, ReadOnlyMemory<byte>.Empty));
            Assert.AreEqual(ConnectionState.Stopped, plain.ClientManager.State);
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
        }

        [Test]
        public void TheLocalClientOfAHostIgnoresTheConnection()
        {
            Server host = StartServer("pod-a", encrypted: true);

            host.Runtime.ClientManager.StartConnection(null, 0, new byte[] { 1, 2, 3 });
            Run(10);

            Assert.AreEqual(ConnectionState.Started, host.Runtime.ClientManager.State);
        }

        [Test]
        public void AttemptsReuseTheGrantUntilTheServerAnswers()
        {
            Server server = StartServer("pod-a", encrypted: true);
            ushort port = server.Port;
            server.Runtime.ServerManager.StopConnection();
            client = CreateClient(encrypted: true);
            client.ClientManager.StartConnection("127.0.0.1", port, tokens.Issue("pod-a", new byte[] { 8 }));
            Run(150);
            Assert.AreNotEqual(ConnectionState.Started, client.ClientManager.State);

            server.Runtime.ServerManager.StartConnection(port);
            Run(60);

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            CollectionAssert.AreEqual(new byte[] { 8 }, server.SeenData.Last());
        }

        [Test]
        public void TransferWithAConnectionMovesTheClientToTheNextEncryptedServer()
        {
            Server serverA = StartServer("pod-a", encrypted: true);
            Server serverB = StartServer("pod-b", encrypted: true);
            client = CreateClient(encrypted: true);
            client.ClientManager.StartConnection("127.0.0.1", serverA.Port, tokens.Issue("pod-a", new byte[] { 1 }));
            Run(20);
            ulong peerOnA = serverA.Started.Single();
            clientEvents.Clear();

            Assert.IsTrue(serverA.Runtime.ServerManager.Transfer(peerOnA, "127.0.0.1", serverB.Port, ReadOnlyMemory<byte>.Empty, tokens.Issue("pod-b", new byte[] { 2 })));
            Run(30);

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            CollectionAssert.AreEqual(new byte[] { 2 }, serverB.SeenData.Single());
            Assert.AreEqual(StopReason.Transferred, serverA.Ended[peerOnA]);
            CollectionAssert.AreEqual(new[]
            {
                $"Transferring 127.0.0.1:{serverB.Port}",
                "Stopped Transferred retry=True",
                "Starting None retry=False",
                "Authenticating None retry=False",
                "Started None retry=False",
            }, clientEvents);
        }

        [Test]
        public void ATransferWithoutAConnectionEndsAnEncryptedClientWithoutRetry()
        {
            Server serverA = StartServer("pod-a", encrypted: true);
            Server serverB = StartServer("pod-b", encrypted: true);
            client = CreateClient(encrypted: true);
            client.ClientManager.StartConnection("127.0.0.1", serverA.Port, tokens.Issue("pod-a", ReadOnlyMemory<byte>.Empty));
            Run(20);
            clientEvents.Clear();

            serverA.Runtime.ServerManager.Transfer(serverA.Started.Single(), "127.0.0.1", serverB.Port, ReadOnlyMemory<byte>.Empty);
            Run(30);

            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
            CollectionAssert.AreEqual(new[] { $"Transferring 127.0.0.1:{serverB.Port}", "Stopped Transferred retry=False" }, clientEvents);
            StringAssert.Contains("sent no connection", errors.Single());
            CollectionAssert.IsEmpty(serverB.SeenData);
        }

        [Test]
        public void ATransferWithAConnectionEndsAPlainClientWithoutRetry()
        {
            Server serverA = StartServer("pod-a", encrypted: false);
            Server serverB = StartServer("pod-b", encrypted: true);
            client = CreateClient(encrypted: false);
            client.ClientManager.StartConnection("127.0.0.1", serverA.Port);
            Run(20);
            clientEvents.Clear();

            serverA.Runtime.ServerManager.Transfer(serverA.Started.Single(), "127.0.0.1", serverB.Port, ReadOnlyMemory<byte>.Empty, tokens.Issue("pod-b", ReadOnlyMemory<byte>.Empty));
            Run(30);

            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
            Assert.AreEqual("Stopped Transferred retry=False", clientEvents.Last());
            StringAssert.Contains("does not take the connection", errors.Single());
        }

        [Test]
        public void AMultiTransportWhoseClientTransportEncryptsNeedsAConnection()
        {
            var multi = new MultiTransportFactory(new ITransportFactory[] { new EncryptedUdpTransportFactory(registry), new UdpTransportFactory() }, new ushort[] { 0, 0 });
            var plainFirst = new MultiTransportFactory(new ITransportFactory[] { new UdpTransportFactory(), new EncryptedUdpTransportFactory(registry) }, new ushort[] { 0, 0 });

            Assert.IsTrue(multi.RequiresConnection);
            Assert.IsTrue(multi.CanUseConnection(tokens.Issue("pod-a", ReadOnlyMemory<byte>.Empty)));
            Assert.IsFalse(plainFirst.RequiresConnection);
            Assert.IsFalse(plainFirst.CanUseConnection(tokens.Issue("pod-a", ReadOnlyMemory<byte>.Empty)));
            Assert.Throws<ArgumentException>(() => plainFirst.CreateConnector("127.0.0.1", 7777, tokens.Issue("pod-a", ReadOnlyMemory<byte>.Empty)));
        }

        private Server StartServer(string target, bool encrypted)
        {
            ITransportFactory factory = encrypted ? new EncryptedUdpTransportFactory(registry, tokens, target) : new UdpTransportFactory();
            var server = new Server(this, factory);
            server.Runtime.ServerManager.StartConnection(0);
            servers.Add(server);
            return server;
        }

        private NetworkRuntime CreateClient(bool encrypted) =>
            CreateClient(encrypted ? new EncryptedUdpTransportFactory(registry) : new UdpTransportFactory());

        private NetworkRuntime CreateClient(ITransportFactory factory)
        {
            var prefabs = new StandalonePrefabs();
            prefabs.Register(PlayerPrefab, Fingerprint, () => new StandaloneEntity(PlayerPrefab, Array.Empty<EntityBehaviour>()));
            NetworkRuntime created = StandaloneRuntime.Create(registry, new NetworkSettings(), factory, prefabs, new StandaloneBehaviours(), new StandaloneBackendTest.MemorySceneFiles(), () => now, log);
            created.ClientManager.OnTransferring += args => clientEvents.Add($"Transferring {args.Address}:{args.Port}");
            created.ClientManager.OnClientConnectionState += args => clientEvents.Add($"{args.State} {args.Reason} retry={args.WillRetry}");
            return created;
        }

        private void Run(int frames)
        {
            for (int index = 0; index < frames; index++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                foreach (Server server in servers)
                {
                    server.Runtime.BeginFrame(now, FrameSeconds);
                }

                client?.BeginFrame(now, FrameSeconds);
                foreach (Server server in servers)
                {
                    server.Runtime.EndFrame();
                }

                client?.EndFrame();
                Thread.Sleep(1);
            }
        }

        private sealed class RequiresConnectionFactory : IConnectionTransportFactory
        {
            private readonly UdpTransportFactory plain = new UdpTransportFactory();

            public int FrameBudget => plain.FrameBudget;

            public bool RequiresConnection => true;

            public bool CanUseConnection(ReadOnlyMemory<byte> connection) => false;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort) => plain.CreateListener(port, out boundPort);

            public ITransportConnector CreateConnector(string address, ushort port) => plain.CreateConnector(address, port);

            public ITransportConnector CreateConnector(string address, ushort port, ReadOnlyMemory<byte> connection) => plain.CreateConnector(address, port);
        }

        private sealed class Server : IServerAuthenticator
        {
            public Server(EncryptedTransportTest test, ITransportFactory factory)
            {
                var prefabs = new StandalonePrefabs();
                prefabs.Register(PlayerPrefab, Fingerprint, () => new StandaloneEntity(PlayerPrefab, Array.Empty<EntityBehaviour>()));
                Runtime = StandaloneRuntime.Create(test.registry, new NetworkSettings(), factory, prefabs, new StandaloneBehaviours(), new StandaloneBackendTest.MemorySceneFiles(), () => test.now, test.log);
                Runtime.ServerManager.Authenticator = this;
                Runtime.ServerManager.OnRemoteConnectionState += OnPeerState;
            }

            public NetworkRuntime Runtime { get; }

            public ushort Port => Runtime.ServerManager.Port;

            public List<byte[]> SeenData { get; } = new List<byte[]>();

            public List<ulong> Started { get; } = new List<ulong>();

            public Dictionary<ulong, StopReason> Ended { get; } = new Dictionary<ulong, StopReason>();

            public void RegisterServer(ServerAuthentication authentication)
            {
            }

            public void OnServerAuthenticating(ServerAuthenticationContext context)
            {
                SeenData.Add(context.ConnectionData.ToArray());
                context.Accept();
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
        }
    }
}
