using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class EntityBehaviourTest
    {
        [Test]
        public void RegisterRunsEachHookOnceInOrder()
        {
            var behaviour = new RecordingBehaviour();

            Register(behaviour);
            Register(behaviour);

            CollectionAssert.AreEqual(new[] { "generated", "rpcs", "state", "input" }, behaviour.Calls);
        }

        [Test]
        public void ARegisteredServerRpcIsFoundByItsMessageId()
        {
            var behaviour = new RecordingBehaviour { ServerRpc = true };

            Register(behaviour);

            Assert.IsTrue(behaviour.TryGetServerRpc(TickPingNetAdapter.Instance.MessageId, out ServerRpc rpc));
            Assert.IsTrue(rpc.RequireOwnership);
            CollectionAssert.AreEqual(new[] { TickPingNetAdapter.Instance.MessageId }, behaviour.ServerRpcIds);
        }

        [Test]
        public void TheSameServerRpcTwiceThrowsWithTheDeclaringType()
        {
            var behaviour = new RecordingBehaviour { ServerRpc = true, ServerRpcTwice = true };

            var error = Assert.Throws<HandlerRegistrationException>(() => Register(behaviour));

            StringAssert.Contains(typeof(RecordingBehaviour).FullName, error.Message);
            StringAssert.Contains("as a server RPC twice", error.Message);
        }

        [Test]
        public void StateOnTheUnreliableChannelIsRejected()
        {
            var behaviour = new RecordingBehaviour { StateCodec = TickPingNetAdapter.Instance };

            var error = Assert.Throws<HandlerRegistrationException>(() => Register(behaviour));

            StringAssert.Contains("not on the reliable-ordered channel", error.Message);
        }

        [Test]
        public void StateOnTheReliableChannelGetsASlot()
        {
            var behaviour = new RecordingBehaviour { StateCodec = AnimatorStateNetAdapter.Instance };

            Register(behaviour);

            Assert.IsNotNull(behaviour.StateSlot);
            Assert.AreEqual(AnimatorStateNetAdapter.Instance.MessageId, behaviour.StateSlot.MessageId);
        }

        [Test]
        public void SendingBeforeAttachThrowsNotSpawned()
        {
            var behaviour = new RecordingBehaviour();

            var error = Assert.Throws<InvalidOperationException>(() => behaviour.SendPing());

            Assert.AreEqual("RecordingBehaviour is not spawned on the client", error.Message);
        }

        [Test]
        public void AServerRpcGoesToTheLinkWithTheBehaviourIndex()
        {
            var behaviour = new RecordingBehaviour();
            var link = new FakeLink { Client = true };
            behaviour.Attach(link, 3);

            SendResult result = behaviour.SendPing();

            Assert.AreEqual(SendResult.Queued, result);
            Assert.AreEqual(1, link.Sent.Count);
            Assert.AreEqual((TickPingNetAdapter.Instance.MessageId, (byte)3, 0UL), link.Sent[0]);
        }

        [Test]
        public void AnObserversRpcOnTheClientOnlyThrowsNotSpawnedOnTheServer()
        {
            var behaviour = new RecordingBehaviour();
            behaviour.Attach(new FakeLink { Client = true }, 0);

            var error = Assert.Throws<InvalidOperationException>(() => behaviour.BroadcastPing());

            Assert.AreEqual("RecordingBehaviour is not spawned on the server", error.Message);
        }

        [Test]
        public void AParameterlessRpcWithoutAGeneratedModelThrows()
        {
            var behaviour = new RecordingBehaviour();
            behaviour.Attach(new FakeLink { Client = true }, 0);

            var error = Assert.Throws<InvalidOperationException>(() => behaviour.SendNamed("Jump"));

            StringAssert.Contains(typeof(RecordingBehaviour).FullName + ".Jump has no generated parameterless RPC model", error.Message);
        }

        [Test]
        public void AParameterlessRpcUsesTheIdOfTheDeclaringType()
        {
            var behaviour = new RecordingBehaviour();
            var link = new FakeLink { Client = true };
            link.Ids.Set(typeof(RecordingBehaviour).FullName, "Jump", 0x0A0B0C0D);
            behaviour.Attach(link, 1);

            behaviour.SendNamed("Jump");

            Assert.AreEqual((0x0A0B0C0Du, (byte)1, 0UL), link.Sent[0]);
        }

        private static void Register(EntityBehaviour behaviour) =>
            behaviour.Register(new RpcMessageIds(), TestObjects.Channels(), TestObjects.StateProtocol(TestObjects.Channels()), new InputRules { Allowed = true });

        private sealed class RecordingBehaviour : EntityBehaviour
        {
            public readonly List<string> Calls = new List<string>();

            public bool ServerRpc { get; set; }

            public bool ServerRpcTwice { get; set; }

            public object StateCodec { get; set; }

            public SendResult SendPing() => SendServerRpc(TickPingNetAdapter.Instance, new TickPing());

            public int BroadcastPing() => SendObserversRpc(TickPingNetAdapter.Instance, new TickPing());

            public SendResult SendNamed(string rpc) => SendServerRpc(rpc);

            protected override void OnRegisterGeneratedRpcs(NetworkRpcs rpc) => Calls.Add("generated");

            protected override void OnRegisterRpcs(NetworkRpcs rpc)
            {
                Calls.Add("rpcs");
                if (ServerRpc)
                {
                    rpc.OnServer(TickPingNetAdapter.Instance, (peerId, ping) => { });
                }

                if (ServerRpcTwice)
                {
                    rpc.OnServer(TickPingNetAdapter.Instance, (peerId, ping) => { });
                }
            }

            protected override void OnRegisterState(NetworkState state)
            {
                Calls.Add("state");
                if (StateCodec is IMessageCodec<AnimatorState> animator)
                {
                    state.Use(animator, new AnimatorState());
                }
                else if (StateCodec is IMessageCodec<TickPing> ping)
                {
                    state.Use(ping, new TickPing());
                }
            }

            protected override void OnRegisterInput(NetworkInput input) => Calls.Add("input");
        }

        private sealed class FakeLink : IBehaviourLink
        {
            public readonly List<(uint MessageId, byte BehaviourIndex, ulong PeerId)> Sent = new List<(uint, byte, ulong)>();

            public readonly RpcMessageIds Ids = new RpcMessageIds();

            public bool Server { get; set; }

            public bool Client { get; set; }

            public bool SpawnedOnServer => Server;

            public bool SpawnedOnClient => Client;

            public bool IsReplaying => false;

            public RpcMessageIds RpcIds(bool server) => Ids;

            public SendResult SendToServer(uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body)
            {
                Sent.Add((messageId, behaviourIndex, 0));
                return SendResult.Queued;
            }

            public int SendToObservers(uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body)
            {
                Sent.Add((messageId, behaviourIndex, 0));
                return 1;
            }

            public SendResult SendToObserver(ulong peerId, uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body)
            {
                Sent.Add((messageId, behaviourIndex, peerId));
                return SendResult.Queued;
            }
        }
    }
}
