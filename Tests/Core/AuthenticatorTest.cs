using System;
using System.Collections.Generic;
using System.Linq;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class AuthenticatorTest
    {
        private const uint CredentialId = 0x3000_0001;
        private const uint VerdictId = 0x3000_0002;

        private static readonly PinCodec Credentials = new PinCodec(CredentialId);
        private static readonly PinCodec Verdicts = new PinCodec(VerdictId);

        private sealed class Pin
        {
            public byte Value { get; set; }
        }

        private sealed class PinCodec : IMessageCodec<Pin>
        {
            private readonly int width;

            public PinCodec(uint messageId, int width = 1)
            {
                MessageId = messageId;
                this.width = width;
            }

            public uint MessageId { get; }

            public ReadOnlyMemory<byte> Encode(Pin value)
            {
                var bytes = new byte[width];
                bytes[0] = value.Value;
                return bytes;
            }

            public void Decode(ReadOnlyMemory<byte> payload, ref Pin value)
            {
                if (payload.Length != 1)
                {
                    throw new MessageDecodeException("a pin is one byte", null);
                }

                value.Value = payload.Span[0];
            }
        }

        private sealed class PinAuthenticator : IServerAuthenticator, IClientAuthenticator
        {
            public readonly List<string> Log = new List<string>();
            public byte Expected = 7;
            public byte Offered = 7;
            public PinCodec OfferedWith = Credentials;

            public void RegisterServer(ServerAuthentication server)
            {
                server.On(Credentials, (context, pin) =>
                {
                    Log.Add($"credential:{pin.Value}");
                    bool matches = pin.Value == Expected;
                    context.Send(Verdicts, new Pin { Value = matches ? (byte)1 : (byte)0 });
                    if (matches)
                    {
                        context.Accept();
                    }
                    else
                    {
                        context.Reject();
                    }
                });
            }

            public void OnServerAuthenticating(ServerAuthenticationContext context) => Log.Add("server-authenticating");

            public void RegisterClient(ClientAuthentication client)
            {
                client.On(Verdicts, (context, pin) => Log.Add($"verdict:{pin.Value}"));
            }

            public void OnClientAuthenticating(ClientAuthenticationContext context)
            {
                Log.Add("client-authenticating");
                context.Send(OfferedWith, new Pin { Value = Offered });
            }
        }

        private sealed class BothModelsAuthenticator : IServerAuthenticator, IClientAuthenticator
        {
            public void RegisterServer(ServerAuthentication server)
            {
                server.On(Credentials, (context, pin) => { });
                server.On(Verdicts, (context, pin) => { });
            }

            public void OnServerAuthenticating(ServerAuthenticationContext context)
            {
            }

            public void RegisterClient(ClientAuthentication client)
            {
                client.On(Verdicts, (context, pin) => { });
                client.On(Credentials, (context, pin) => { });
            }

            public void OnClientAuthenticating(ClientAuthenticationContext context)
            {
            }
        }

        private sealed class Rig
        {
            public readonly ServerSceneContentTest.FakeHost ServerHost = new ServerSceneContentTest.FakeHost();
            public readonly ServerManager Server;
            public readonly ClientManager Client;
            public readonly List<ConnectionStateArgs> ClientStates = new List<ConnectionStateArgs>();
            public readonly List<ReceiveDroppedArgs> ServerDropped = new List<ReceiveDroppedArgs>();
            private TimeSpan now;

            public Rig()
            {
                Schema schema = TestObjects.SchemaWith(
                    new MessageSchema(TestObjects.GreetingId, 0xF00D, new ulong[] { 0xF00D }),
                    new MessageSchema(CredentialId, 0xF00E, new ulong[] { 0xF00E }),
                    new MessageSchema(VerdictId, 0xF00F, new ulong[] { 0xF00F }));
                MessageChannels channels = TestObjects.Channels();
                channels.Set(CredentialId, Channel.ReliableOrdered);
                channels.Set(VerdictId, Channel.ReliableOrdered);
                var log = new NetworkLog(exception => throw exception, message => { });
                var factory = new ClientManagerTest.Factory();
                Server = new ServerManager(
                    schema,
                    new SessionLimits(),
                    new SessionConfig(),
                    TestBundles.Protocol(channels),
                    TestObjects.Protocol(channels),
                    TestObjects.StateProtocol(channels),
                    TestObjects.TransformProtocol(channels),
                    TestObjects.SceneProtocol(channels),
                    TestObjects.ClockProtocol(),
                    TestObjects.InputProtocol(),
                    new RpcMessageIds(),
                    factory,
                    8,
                    new ServerSceneContentTest.SceneBackend(ServerHost),
                    ServerHost,
                    log);
                Client = new ClientManager(
                    schema,
                    new SessionLimits(),
                    new SessionConfig(),
                    TestBundles.Protocol(channels),
                    TestObjects.Protocol(channels),
                    TestObjects.StateProtocol(channels),
                    TestObjects.TransformProtocol(channels),
                    TestObjects.SceneProtocol(channels),
                    TestObjects.ClockProtocol(),
                    new ClockSettings(),
                    TestObjects.InputProtocol(),
                    new TimeManager(30, 3, TimingMode.Tick),
                    new RpcMessageIds(),
                    new ReconnectPolicy(),
                    factory,
                    Server,
                    new ClientEntitiesTest.FakeClientBackend(new List<string>()),
                    new ClientManagerTest.NoScenes(),
                    new ClientPredictionTest.FakeBackend(),
                    () => now,
                    log);
                Client.OnClientConnectionState += ClientStates.Add;
                Server.OnReceiveDropped += ServerDropped.Add;
            }

            public void Connect()
            {
                Server.StartConnection(0);
                Client.StartConnection("unused.invalid", 1);
            }

            public void Run(int steps)
            {
                for (int step = 0; step < steps; step++)
                {
                    now += TimeSpan.FromMilliseconds(16);
                    Server.Tick(now);
                    Client.Tick(now);
                    Server.Flush();
                    Client.Flush();
                }
            }
        }

        [Test]
        public void TheLocalClientOfAHostGoesThroughTheAuthenticator()
        {
            var rig = new Rig();
            var authenticator = new PinAuthenticator();
            rig.Server.Authenticator = authenticator;
            rig.Client.Authenticator = authenticator;
            rig.Connect();
            rig.Run(10);

            Assert.AreEqual(ConnectionState.Started, rig.Client.State);
            CollectionAssert.AreEquivalent(new[] { "server-authenticating", "client-authenticating", "credential:7", "verdict:1" }, authenticator.Log);
            Assert.Less(authenticator.Log.IndexOf("credential:7"), authenticator.Log.IndexOf("verdict:1"));
            CollectionAssert.AreEqual(
                new[] { ConnectionState.Starting, ConnectionState.Authenticating, ConnectionState.Started },
                rig.ClientStates.Select(args => args.State));
        }

        [Test]
        public void AWrongPinIsRejectedAndTheClientIsToldWhy()
        {
            var rig = new Rig();
            var authenticator = new PinAuthenticator { Offered = 3 };
            rig.Server.Authenticator = authenticator;
            rig.Client.Authenticator = authenticator;
            rig.Connect();
            rig.Run(10);

            ConnectionStateArgs stop = rig.ClientStates.Single(args => args.State == ConnectionState.Stopped);
            Assert.AreEqual(StopReason.ServerClosed, stop.Reason);
            Assert.AreEqual(SessionCloseReason.AuthenticationFailed, stop.CloseReason);
            Assert.IsFalse(stop.WillRetry);
            CollectionAssert.Contains(authenticator.Log, "verdict:0");
        }

        [Test]
        public void AnUndecodableCredentialIsDroppedAndThePeerKeepsWaiting()
        {
            var rig = new Rig();
            var authenticator = new PinAuthenticator { OfferedWith = new PinCodec(CredentialId, 2) };
            rig.Server.Authenticator = authenticator;
            rig.Client.Authenticator = authenticator;
            rig.Connect();
            rig.Run(10);

            Assert.AreEqual(1, rig.ServerDropped.Count);
            CollectionAssert.DoesNotContain(authenticator.Log, "credential:7");
            Assert.AreEqual(ConnectionState.Authenticating, rig.Client.State);
        }

        [Test]
        public void TheAuthenticatorChangesOnlyWhileStopped()
        {
            var rig = new Rig();
            rig.Connect();

            Assert.Throws<InvalidOperationException>(() => rig.Server.Authenticator = new PinAuthenticator());
            Assert.Throws<InvalidOperationException>(() => rig.Client.Authenticator = new PinAuthenticator());
            Assert.IsNull(rig.Server.Authenticator);
            Assert.IsNull(rig.Client.Authenticator);
        }

        [Test]
        public void AnAuthenticationModelWithAnotherHandlerIsRefusedEitherWay()
        {
            var taken = new Rig();
            taken.Server.Dispatcher.Register(CredentialId, (peerId, payload) => { });
            var first = new Rig();
            first.Client.Authenticator = new PinAuthenticator();

            Assert.Throws<HandlerRegistrationException>(() => taken.Server.Authenticator = new PinAuthenticator());
            Assert.IsNull(taken.Server.Authenticator);
            Assert.Throws<HandlerRegistrationException>(() => first.Client.Dispatcher.Register(VerdictId, (peerId, payload) => { }));
        }

        [Test]
        public void ARegistrationThatFailsHalfwayLeavesNoAuthenticationModel()
        {
            var rig = new Rig();
            rig.Server.Dispatcher.Register(VerdictId, (peerId, payload) => { });
            rig.Client.Dispatcher.Register(CredentialId, (peerId, payload) => { });

            Assert.Throws<HandlerRegistrationException>(() => rig.Server.Authenticator = new BothModelsAuthenticator());
            Assert.Throws<HandlerRegistrationException>(() => rig.Client.Authenticator = new BothModelsAuthenticator());

            Assert.DoesNotThrow(() => rig.Server.Dispatcher.Register(CredentialId, (peerId, payload) => { }));
            Assert.DoesNotThrow(() => rig.Client.Dispatcher.Register(VerdictId, (peerId, payload) => { }));
            Assert.IsNull(rig.Server.Authenticator);
            Assert.IsNull(rig.Client.Authenticator);
        }

        [Test]
        public void RemovingTheAuthenticatorFreesItsModelsAndAcceptsAtReady()
        {
            var rig = new Rig();
            var authenticator = new PinAuthenticator();
            rig.Server.Authenticator = authenticator;
            rig.Client.Authenticator = authenticator;

            rig.Server.Authenticator = null;
            rig.Client.Authenticator = null;
            rig.Server.Dispatcher.Register(CredentialId, (peerId, payload) => { });
            rig.Client.Dispatcher.Register(VerdictId, (peerId, payload) => { });
            rig.Connect();
            rig.Run(10);

            Assert.AreEqual(ConnectionState.Started, rig.Client.State);
            CollectionAssert.IsEmpty(authenticator.Log);
        }
    }
}
