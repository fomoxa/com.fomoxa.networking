using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ClientPredictionTest
    {
        [Test]
        public void TwoEntitiesInOneWorldCaptureLoadAndStepItOncePerTick()
        {
            var rig = new Rig();
            var world = new FakeWorld();
            Mover first = rig.Spawn(world);
            Mover second = rig.Spawn(world);
            rig.StartPredicting(first, second);
            rig.Tick(3);
            rig.Tick(4);

            Assert.AreEqual(3, world.Saves);

            int requests = rig.Backend.WorldRequests;
            rig.Mismatch(first, 3);
            rig.Tick(5);

            Assert.AreEqual(1, world.Loads);
            Assert.AreEqual(1, world.Steps);
            Assert.AreEqual(5, world.Saves);
            Assert.AreEqual(requests + 4, rig.Backend.WorldRequests);
        }

        [Test]
        public void EntitiesInSeparateWorldsReplayOnlyTheirOwnWorld()
        {
            var rig = new Rig();
            var left = new FakeWorld();
            var right = new FakeWorld();
            Mover first = rig.Spawn(left);
            Mover second = rig.Spawn(right);
            rig.StartPredicting(first, second);
            rig.Tick(3);
            rig.Tick(4);

            rig.Mismatch(first, 3);
            rig.Tick(5);

            Assert.AreEqual(1, left.Loads);
            Assert.AreEqual(1, left.Steps);
            Assert.AreEqual(0, right.Loads);
            Assert.AreEqual(0, right.Steps);
        }

        [Test]
        public void ASharedWorldJoinsTheWorldsOfTwoEntitiesIntoOneGroup()
        {
            var rig = new Rig();
            var a = new FakeWorld();
            var shared = new FakeWorld();
            var c = new FakeWorld();
            Mover first = rig.Spawn(a, shared);
            Mover second = rig.Spawn(shared, c);
            rig.StartPredicting(first, second);
            rig.Tick(3);
            rig.Tick(4);

            rig.Mismatch(first, 3);
            rig.Tick(5);

            foreach (FakeWorld world in new[] { a, shared, c })
            {
                Assert.AreEqual(1, world.Loads);
                Assert.AreEqual(1, world.Steps);
                Assert.AreEqual(5, world.Saves);
            }
        }

        [Test]
        public void AReplayThatLoadedRestoresRecordsAndPublishesTheTrackerOfEachWorld()
        {
            var rig = new Rig();
            var world = new FakeWorld();
            var tracker = new FakeTracker("A", new List<string>());
            rig.Backend.Trackers[world] = tracker;
            Mover mover = rig.Spawn(world);
            rig.StartPredicting(mover);
            rig.Tick(3);
            rig.Tick(4);
            tracker.Calls.Clear();

            rig.Mismatch(mover, 3);
            rig.Tick(5);

            CollectionAssert.AreEqual(new[] { "A restore 3", "A query", "A record 4", "A publish" }, tracker.Calls);
        }

        [Test]
        public void AReplayWithoutALoadQueriesAndPublishesButDoesNotRestoreOrRecord()
        {
            var rig = new Rig();
            var world = new FakeWorld();
            var tracker = new FakeTracker("A", new List<string>());
            rig.Backend.Trackers[world] = tracker;
            Mover mover = rig.Spawn(world);
            rig.Tick(1);
            rig.Tick(2);
            mover.InputSlot.HoldPending(1, Value.Encode(0));

            rig.Tick(3);

            CollectionAssert.AreEqual(new[] { "A query", "A publish" }, tracker.Calls);
        }

        [Test]
        public void TheTrackersOfAGroupAreCalledWorldByWorldAtEachPoint()
        {
            var rig = new Rig();
            var calls = new List<string>();
            var a = new FakeWorld();
            var b = new FakeWorld();
            rig.Backend.Trackers[a] = new FakeTracker("A", calls);
            rig.Backend.Trackers[b] = new FakeTracker("B", calls);
            Mover mover = rig.Spawn(a, b);
            rig.StartPredicting(mover);
            rig.Tick(3);
            rig.Tick(4);
            calls.Clear();

            rig.Mismatch(mover, 3);
            rig.Tick(5);

            CollectionAssert.AreEqual(
                new[] { "A restore 3", "B restore 3", "A query", "A record 4", "B query", "B record 4", "A publish", "B publish" },
                calls);
        }

        [Test]
        public void AWorldWithoutATrackerStillReplays()
        {
            var rig = new Rig();
            var calls = new List<string>();
            var tracked = new FakeWorld();
            var untracked = new FakeWorld();
            rig.Backend.Trackers[tracked] = new FakeTracker("A", calls);
            Mover mover = rig.Spawn(tracked, untracked);
            rig.StartPredicting(mover);
            rig.Tick(3);
            rig.Tick(4);
            calls.Clear();

            rig.Mismatch(mover, 3);
            rig.Tick(5);

            Assert.AreEqual(1, untracked.Loads);
            Assert.AreEqual(1, untracked.Steps);
            CollectionAssert.AreEqual(new[] { "A restore 3", "A query", "A record 4", "A publish" }, calls);
        }

        [Test]
        public void AnEntityWithoutAWorldReconcilesOnItsOwn()
        {
            var rig = new Rig();
            Mover mover = rig.Spawn();

            rig.StartPredicting(mover);

            Assert.IsTrue(mover.InputSlot.Predicting);
            Assert.AreEqual(0, rig.Backend.HistoryRequests);
        }

        private sealed class Rig
        {
            public readonly ClientEntitiesTest.World World = new ClientEntitiesTest.World();
            public readonly FakeBackend Backend = new FakeBackend();
            public readonly ClientPrediction Prediction;

            public Rig()
            {
                World.Client.InputRules.Allowed = true;
                Prediction = new ClientPrediction(World.Client, World.ClientSession, World.ClientDispatcher, TestObjects.InputProtocol(), Backend, new NetworkLog(exception => throw exception, message => { }))
                {
                    SimulatesPhysics = true,
                };
            }

            public Mover Spawn(params FakeWorld[] worlds)
            {
                var mover = new Mover(World.ClientCalls);
                World.Backend.CreateBehaviour = () => mover;
                World.Server.Spawn(World.Served(), World.ClientObjects.LocalPeerId);
                World.Run(10);
                Backend.Worlds[World.Backend.Created[World.Backend.Created.Count - 1]] = worlds;
                return mover;
            }

            public void StartPredicting(params Mover[] movers)
            {
                Tick(1);
                foreach (Mover mover in movers)
                {
                    mover.InputSlot.HoldPending(1, Value.Encode(0));
                }

                Tick(2);
            }

            public void Mismatch(Mover mover, uint tick) => mover.InputSlot.HoldPending(tick, Value.Encode(999));

            public void Tick(uint tick)
            {
                Prediction.Predict(tick, true, 1f / 30);
                Prediction.CapturePredicted(tick);
            }
        }

        private sealed class Mover : ClientEntitiesTest.RecordingBehaviour
        {
            public Mover(List<string> calls)
                : base("mover", calls)
            {
            }

            public int Position { get; private set; }

            protected override void OnRegisterInput(NetworkInput input)
            {
                input.Use(new ValueCodec(0xC0DE0001), value => value.Number = 1, (value, context) => Position += value.Number);
                input.Reconcile(new ValueCodec(0xC0DE0002), state => state.Number = Position, state => Position = state.Number);
            }
        }

        private sealed class Value
        {
            public int Number;

            public static byte[] Encode(int number)
            {
                var bytes = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(bytes, number);
                return bytes;
            }
        }

        private sealed class ValueCodec : IMessageCodec<Value>
        {
            public ValueCodec(uint messageId)
            {
                MessageId = messageId;
            }

            public uint MessageId { get; }

            public ReadOnlyMemory<byte> Encode(Value value) => Value.Encode(value.Number);

            public void Decode(ReadOnlyMemory<byte> payload, ref Value value) =>
                value.Number = BinaryPrimitives.ReadInt32LittleEndian(payload.Span);
        }

        internal sealed class FakeWorld : IPhysicsSimulation
        {
            public int Steps { get; private set; }

            public int Saves { get; private set; }

            public int Loads { get; private set; }

            public PhysicsBackend Backend => PhysicsBackend.Rapier;

            public void Step(float seconds) => Steps++;

            public PhysicsSnapshot CreateSnapshot() => new Snapshot();

            public void Save(PhysicsSnapshot into) => Saves++;

            public void Load(PhysicsSnapshot from) => Loads++;

            private sealed class Snapshot : PhysicsSnapshot
            {
            }
        }

        internal sealed class FakeTracker : IContactTracker
        {
            private readonly string name;

            public FakeTracker(string name, List<string> calls)
            {
                this.name = name;
                Calls = calls;
            }

            public List<string> Calls { get; }

            public void Query() => Calls.Add($"{name} query");

            public void Record(uint tick, int capacity) => Calls.Add($"{name} record {tick}");

            public void Restore(uint tick) => Calls.Add($"{name} restore {tick}");

            public void Publish() => Calls.Add($"{name} publish");
        }

        internal sealed class FakeBackend : IClientPredictionBackend
        {
            public readonly Dictionary<INetworkEntity, FakeWorld[]> Worlds = new Dictionary<INetworkEntity, FakeWorld[]>();
            public readonly Dictionary<IPhysicsSimulation, FakeTracker> Trackers = new Dictionary<IPhysicsSimulation, FakeTracker>();
            private readonly PhysicsHistories histories = new PhysicsHistories();

            public int HistoryRequests { get; private set; }

            public int WorldRequests { get; private set; }

            public void WorldsOf(INetworkEntity entity, List<IPhysicsSimulation> worlds)
            {
                WorldRequests++;
                if (Worlds.TryGetValue(entity, out FakeWorld[] found))
                {
                    worlds.AddRange(found);
                }
            }

            public PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity)
            {
                HistoryRequests++;
                return histories.Of(world, capacity);
            }

            public void PlaceProxy(INetworkEntity entity)
            {
            }

            public void EndProxy(INetworkEntity entity)
            {
            }

            public void ForgetDestroyedProxies()
            {
            }

            public void BeginCorrection(INetworkEntity entity)
            {
            }

            public void EndCorrection(INetworkEntity entity)
            {
            }

            public IContactTracker TrackerOf(IPhysicsSimulation world) =>
                Trackers.TryGetValue(world, out FakeTracker tracker) ? tracker : null;
        }
    }
}
