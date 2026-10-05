using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class InputTest
    {
        private const uint PrefabId = 0xA7;

        [Test]
        public void TheServerTakesTheInputOfEachTickAndRepeatsTheLastWhenOneIsMissing()
        {
            var rig = new Rig();
            Peer client = rig.Connect();
            uint objectId = rig.SpawnOwnedBy(client);
            rig.Clock.Tick = 100;

            client.Send(objectId, 101, 7, 6);
            rig.Run(3);

            AssertTake(rig, objectId, 100, 6, false);
            AssertTake(rig, objectId, 101, 7, false);
            AssertTake(rig, objectId, 102, 7, true, 1);
            AssertTake(rig, objectId, 103, 7, true, 2);
            Assert.AreEqual(0, rig.Inputs.DroppedOf(client.PeerId));
        }

        [Test]
        public void RedundantFramesFillTheTickOfALostPacket()
        {
            var rig = new Rig();
            Peer client = rig.Connect();
            uint objectId = rig.SpawnOwnedBy(client);
            rig.Clock.Tick = 100;

            client.Send(objectId, 100, 1);
            client.Send(objectId, 102, 3, 2, 1);
            rig.Run(3);

            AssertTake(rig, objectId, 100, 1, false);
            AssertTake(rig, objectId, 101, 2, false);
            AssertTake(rig, objectId, 102, 3, false);
        }

        [Test]
        public void LateAndEarlyInputsAreDroppedAndCounted()
        {
            var rig = new Rig();
            Peer client = rig.Connect();
            uint objectId = rig.SpawnOwnedBy(client);
            rig.Clock.Tick = 100;

            client.Send(objectId, 99, 1, 0);
            client.Send(objectId, 131, 2);
            client.Send(objectId, 130, 3);
            rig.Run(3);

            Assert.AreEqual(2, rig.Inputs.DroppedOf(client.PeerId));
            Assert.IsFalse(rig.Inputs.TakeInput(objectId, 0, 100, out _, out _, out _));
            AssertTake(rig, objectId, 130, 3, false);
        }

        [Test]
        public void ALaterCopyOfATickReplacesTheBufferedInput()
        {
            var rig = new Rig();
            Peer client = rig.Connect();
            uint objectId = rig.SpawnOwnedBy(client);
            rig.Clock.Tick = 100;

            client.Send(objectId, 105, 1);
            client.Send(objectId, 105, 2);
            rig.Run(3);

            AssertTake(rig, objectId, 105, 2, false);
        }

        [Test]
        public void InputFromAPeerThatDoesNotOwnTheObjectIsRejectedAndInputToAnUnknownObjectIsIgnored()
        {
            var rig = new Rig();
            Peer owner = rig.Connect();
            Peer other = rig.Connect();
            uint objectId = rig.SpawnOwnedBy(owner);
            var rejected = new List<InputRejectedArgs>();
            rig.Inputs.OnInputRejected += rejected.Add;
            rig.Clock.Tick = 100;

            other.Send(objectId, 101, 1);
            other.Send(objectId + 50, 101, 1);
            rig.Run(3);

            Assert.AreEqual(1, rejected.Count);
            Assert.AreEqual(other.PeerId, rejected[0].PeerId);
            Assert.AreEqual(objectId, rejected[0].ObjectId);
            Assert.AreEqual(0, rejected[0].BehaviourIndex);
            Assert.IsFalse(rig.Inputs.TakeInput(objectId, 0, 101, out _, out _, out _));
            Assert.IsFalse(rig.Inputs.TakeInput(objectId + 50, 0, 101, out _, out _, out _));
        }

        [Test]
        public void AnOwnerChangeAndADespawnForgetTheInputsOfTheObject()
        {
            var rig = new Rig();
            Peer first = rig.Connect();
            Peer second = rig.Connect();
            uint objectId = rig.SpawnOwnedBy(first);
            rig.Clock.Tick = 100;
            first.Send(objectId, 101, 2, 1);
            rig.Run(3);
            AssertTake(rig, objectId, 100, 1, false);

            Assert.IsTrue(rig.Objects.ChangeOwner(objectId, second.PeerId));

            Assert.IsFalse(rig.Inputs.TakeInput(objectId, 0, 101, out _, out _, out _));

            second.Send(objectId, 102, 5);
            first.Send(objectId, 102, 9);
            rig.Run(3);
            AssertTake(rig, objectId, 102, 5, false);

            Assert.IsTrue(rig.Objects.Despawn(objectId));

            Assert.IsFalse(rig.Inputs.TakeInput(objectId, 0, 103, out _, out _, out _));
        }

        [Test]
        public void TheInputLeadOfAPeerReachesItsClockAndExpires()
        {
            var rig = new Rig();
            Peer client = rig.Connect();
            uint objectId = rig.SpawnOwnedBy(client);
            rig.Clock.Tick = 100;

            client.Send(objectId, 103, 1);
            rig.Run(10);

            Assert.IsTrue(client.Clock.Estimator.Synced);
            Assert.IsTrue(client.Clock.Estimator.HasInputLead);

            rig.Clock.Tick = 100 + (uint)rig.Inputs.MaxInputLead + 1;
            rig.Run(10);

            Assert.IsFalse(client.Clock.Estimator.HasInputLead);
        }

        [Test]
        public void AStoppedPeerAndAStoppedServerForgetEverything()
        {
            var rig = new Rig();
            Peer client = rig.Connect();
            uint objectId = rig.SpawnOwnedBy(client);
            rig.Objects.DespawnWithOwner = id => false;
            rig.Clock.Tick = 100;
            client.Send(objectId, 99, 1);
            client.Send(objectId, 101, 2);
            rig.Run(3);
            ulong peerId = client.PeerId;
            Assert.AreEqual(1, rig.Inputs.DroppedOf(peerId));

            client.Session.Stop();
            rig.Run(5);

            Assert.AreEqual(0, rig.Inputs.DroppedOf(peerId));
            Assert.IsFalse(rig.Inputs.TakeInput(objectId, 0, 101, out _, out _, out _));

            rig.Server.Stop();

            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Inputs.MaxInputLead = 0);
        }

        private static void AssertTake(Rig rig, uint objectId, uint tick, uint value, bool repeated, uint repeatedTicks = 0)
        {
            Assert.IsTrue(rig.Inputs.TakeInput(objectId, 0, tick, out ReadOnlyMemory<byte> input, out bool wasRepeated, out uint ticksRepeated));
            Assert.AreEqual(value, BinaryPrimitives.ReadUInt32LittleEndian(input.Span));
            Assert.AreEqual(repeated, wasRepeated);
            Assert.AreEqual(repeatedTicks, ticksRepeated);
        }

        private sealed class Rig
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly ServerSession Server;
            public readonly ServerObjects Objects;
            public readonly ServerClock Clock;
            public readonly ServerInputs Inputs;
            public readonly List<Peer> Clients = new List<Peer>();
            public readonly List<ulong> StartedPeers = new List<ulong>();
            public TimeSpan Now = TimeSpan.FromSeconds(1);

            public Rig()
            {
                Server = new ServerSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestObjects.Schema()), TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ServerObjects(Server, TestObjects.Protocol(TestObjects.Channels()), objectId => new SpawnData(0xF1, Vector3.Zero, Quaternion.Identity, Vector3.One, null));
                Clock = new ServerClock(Server, TestObjects.ClockProtocol());
                Inputs = new ServerInputs(Server, Objects, TestObjects.InputProtocol(), Clock);
                Server.OnRemoteConnectionState += args =>
                {
                    if (args.State == ConnectionState.Started)
                    {
                        StartedPeers.Add(args.PeerId);
                    }
                };
                Server.Start(Listener);
            }

            public Peer Connect()
            {
                var peer = new Peer(this);
                Clients.Add(peer);
                Run(10);
                return peer;
            }

            public uint SpawnOwnedBy(Peer owner)
            {
                uint objectId = Objects.Spawn(PrefabId, owner.PeerId);
                Run(3);
                return objectId;
            }

            public void Run(int steps)
            {
                for (int step = 0; step < steps; step++)
                {
                    Now += TimeSpan.FromMilliseconds(20);
                    Server.Tick(Now);
                    foreach (Peer client in Clients)
                    {
                        client.Session.Tick(Now);
                        client.Clock.Update(Now);
                        client.Clock.Estimator.Advance(Now);
                    }

                    Server.Flush();
                    foreach (Peer client in Clients)
                    {
                        client.Session.Flush();
                    }
                }
            }
        }

        private sealed class Peer
        {
            public readonly ClientSession Session;
            public readonly ClientObjects Objects;
            public readonly ClientClock Clock;
            private readonly Rig rig;
            private readonly int index;
            private readonly InputFrames frames = new InputFrames();

            public Peer(Rig rig)
            {
                this.rig = rig;
                index = rig.Clients.Count;
                Session = new ClientSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestObjects.Schema()), TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ClientObjects(Session, TestObjects.Protocol(TestObjects.Channels()), new NoSpawner());
                Clock = new ClientClock(Session, TestObjects.ClockProtocol(), new ClockSettings { PingInterval = TimeSpan.FromMilliseconds(40) });
                Objects.OnLocalPeerAssigned += peerId => Clock.Estimator.SetTickRate(Objects.ServerTickRate);
                Session.Start(rig.Listener.Connect(), rig.Now);
            }

            public ulong PeerId => rig.StartedPeers[index];

            public void Send(uint objectId, uint tick, params uint[] newestFirst)
            {
                frames.Tick = tick;
                frames.Frames.Clear();
                foreach (uint value in newestFirst)
                {
                    var bytes = new byte[4];
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
                    frames.Frames.Add(bytes);
                }

                Session.SendToObject(TestObjects.InputFramesId, objectId, 0, InputFramesNetAdapter.Instance.Encode(frames).Span);
            }
        }

        private sealed class NoSpawner : IObjectSpawner
        {
            public SpawnResult Spawn(in SpawnedObject spawned) => SpawnResult.Spawned;

            public void Despawn(uint objectId)
            {
            }
        }
    }
}
