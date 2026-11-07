using System;
using System.Collections.Generic;
using System.Numerics;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class PhysicsTest
    {
        [Test]
        public void TheDefaultQueryFilterTakesEveryLayerAndTriggersAndExcludesNoBody()
        {
            QueryFilter all = QueryFilter.All;
            var some = new QueryFilter(1 << 3, new BodyHandle(7), false);

            Assert.AreEqual((-1, 0, true), (all.LayerMask, all.ExcludeBody.Value, all.IncludeTriggers));
            Assert.AreEqual((8, 7, false), (some.LayerMask, some.ExcludeBody.Value, some.IncludeTriggers));
        }

        [Test]
        public void TheShortQueriesUseTheDefaultFilterAndASphereOrACircle()
        {
            var world = new PointWorld();
            var world2D = new PointWorld2D();

            world.Raycast(Vector3.Zero, Vector3.UnitX, 10f, out _);
            QueryFilter ray = world.LastFilter;
            world.Overlap(new Vector3(1f, 2f, 3f), 0.5f, new BodyHandle[1]);
            world2D.Raycast(Vector2.Zero, Vector2.UnitX, 10f, out _);
            QueryFilter ray2D = world2D.LastFilter;
            world2D.Overlap(new Vector2(1f, 2f), 0.5f, new BodyHandle[1]);

            foreach (QueryFilter filter in new[] { ray, world.LastFilter, ray2D, world2D.LastFilter })
            {
                Assert.AreEqual((-1, 0, true), (filter.LayerMask, filter.ExcludeBody.Value, filter.IncludeTriggers));
            }

            Assert.AreEqual((ShapeKind.Sphere, 0.5f), (world.LastShape.Kind, world.LastShape.Radius));
            Assert.AreEqual((new Vector3(1f, 2f, 3f), Quaternion.Identity), (world.LastPosition, world.LastRotation));
            Assert.AreEqual((ShapeKind2D.Circle, 0.5f), (world2D.LastShape.Kind, world2D.LastShape.Radius));
            Assert.AreEqual((new Vector2(1f, 2f), 0f), (world2D.LastPosition, world2D.LastRotation));
        }

        [Test]
        public void ContactPointsGoThroughTheWorldWithTheDefaultFilterOrTheOneGiven()
        {
            var world = new PointWorld();
            var world2D = new PointWorld2D();
            var points = new List<ContactPointInfo> { default, default };
            var points2D = new List<ContactPointInfo2D>();
            var layer = new QueryFilter(1 << 4, default, false);

            int count = world.GetContactPoints(new BodyHandle(3), default, points);
            QueryFilter all = world.LastFilter;
            new PhysicsBody(world, new BodyHandle(3)).GetContactPoints(new BodyHandle(5), layer, points);
            int count2D = new PhysicsBody2D(world2D, new BodyHandle(4)).GetContactPoints(default, points2D);

            Assert.AreEqual((1, -1, 0, true), (count, all.LayerMask, all.ExcludeBody.Value, all.IncludeTriggers));
            Assert.AreEqual((1 << 4, false), (world.LastFilter.LayerMask, world.LastFilter.IncludeTriggers));
            CollectionAssert.AreEqual(new[] { new ContactPointInfo(new Vector3(3f, 5f, 0f), Vector3.UnitY, 0.25f) }, points);
            Assert.AreEqual((1, -1), (count2D, world2D.LastFilter.LayerMask));
            CollectionAssert.AreEqual(new[] { new ContactPointInfo2D(new Vector2(4f, 0f), Vector2.UnitY, 0.25f) }, points2D);
        }

        [Test]
        public void TheHistorySavesTheWorldAfterEachTickAndLoadsItBack()
        {
            var world = new PointWorld();
            BodyHandle body = world.CreateBody(new BodyDesc(BodyKind.Dynamic, BodyShape.Sphere(0.5f), Vector3.Zero, Quaternion.Identity, 1f));
            world.SetBody(body, new BodyState { Rotation = Quaternion.Identity, Velocity = Vector3.UnitX });
            var history = new PhysicsHistory(world, 8);

            for (uint tick = 1; tick <= 5; tick++)
            {
                world.Step(1f);
                history.Save(tick);
            }

            Assert.IsTrue(history.Load(3));
            Assert.AreEqual(new Vector3(3f, 0f, 0f), world.GetBody(body).Position);
            Assert.IsTrue(history.Has(5));
            Assert.IsFalse(history.Has(6));
        }

        [Test]
        public void TheHistoryIsARingThatReusesItsSnapshots()
        {
            var world = new PointWorld();
            var history = new PhysicsHistory(world, 4);

            for (uint tick = 1; tick <= 10; tick++)
            {
                history.Save(tick);
            }

            Assert.AreEqual(4, history.Capacity);
            Assert.AreEqual(4, world.SnapshotsCreated);
            Assert.IsFalse(history.Has(6));
            Assert.IsFalse(history.Load(6));
            Assert.IsTrue(history.Has(7));
            Assert.IsTrue(history.Has(10));

            history.Clear();

            Assert.IsFalse(history.Has(10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsHistory(world, 0));
            Assert.Throws<ArgumentNullException>(() => new PhysicsHistory(null, 4));
        }

        [Test]
        public void APhysicsBodyReadsAndWritesItsBodyThroughTheWorld()
        {
            var world = new PointWorld();
            BodyHandle handle = world.CreateBody(new BodyDesc(BodyKind.Dynamic, BodyShape.Box(Vector3.One), Vector3.Zero, Quaternion.Identity, 1f));
            var body = new PhysicsBody(world, handle);

            body.Velocity = new Vector3(2f, 0f, 0f);
            body.Position = new Vector3(1f, 0f, 0f);
            world.Step(0.5f);

            Assert.IsTrue(body.IsValid);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), body.Position);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), body.State.Velocity);
            Assert.IsFalse(body.IsKinematic);
            Assert.AreEqual(1f, body.Mass);

            body.AddTorque(new Vector3(1f, 0f, 0f));
            body.AddTorqueImpulse(new Vector3(0f, 2f, 0f));
            body.AddForceAtPoint(new Vector3(3f, 0f, 0f), new Vector3(0f, 1f, 0f));
            body.AddImpulseAtPoint(new Vector3(0f, 0f, 4f), new Vector3(1f, 0f, 0f));

            Assert.AreEqual(new Vector3(1f, 2f, 0f), world.Turned);
            Assert.AreEqual(new Vector3(3f, 0f, 4f), world.PushedAtPoint);
            Assert.AreEqual(new Vector3(1f, 1f, 0f), world.Points);

            world.RemoveBody(handle);

            Assert.IsFalse(body.IsValid);
            Assert.IsFalse(default(PhysicsBody).IsValid);
        }

        [Test]
        public void TheHistoryRewindsEitherKindOfWorldThroughTheSharedSimulationInterface()
        {
            var world2D = new PointWorld2D();
            BodyHandle body2D = world2D.CreateBody(new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Circle(0.5f), Vector2.Zero, 0f, 1f));
            world2D.SetBody(body2D, new BodyState2D { Velocity = Vector2.UnitY, AngularVelocity = 0.5f });
            var world3D = new PointWorld();
            BodyHandle body3D = world3D.CreateBody(new BodyDesc(BodyKind.Dynamic, BodyShape.Sphere(0.5f), Vector3.Zero, Quaternion.Identity, 1f));
            world3D.SetBody(body3D, new BodyState { Rotation = Quaternion.Identity, Velocity = Vector3.UnitX });
            var histories = new[] { new PhysicsHistory(world2D, 8), new PhysicsHistory(world3D, 8) };
            IPhysicsSimulation[] simulations = { world2D, world3D };

            for (uint tick = 1; tick <= 5; tick++)
            {
                for (int index = 0; index < simulations.Length; index++)
                {
                    simulations[index].Step(1f);
                    histories[index].Save(tick);
                }
            }

            Assert.IsTrue(histories[0].Load(2));
            Assert.IsTrue(histories[1].Load(4));
            Assert.AreEqual(new Vector2(0f, 2f), world2D.GetBody(body2D).Position);
            Assert.AreEqual(1f, world2D.GetBody(body2D).Rotation, 1e-6f);
            Assert.AreEqual(new Vector3(4f, 0f, 0f), world3D.GetBody(body3D).Position);
        }

        [Test]
        public void APhysicsBody2DReadsAndWritesItsBodyThroughTheWorld()
        {
            var world = new PointWorld2D();
            BodyHandle handle = world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, BodyShape2D.Box(Vector2.One), new Vector2(1f, 2f), 0.25f, 3f));
            var body = new PhysicsBody2D(world, handle);

            Assert.AreEqual(new Vector2(1f, 2f), body.Position);
            Assert.AreEqual(0.25f, body.Rotation);
            body.Velocity = new Vector2(2f, 0f);
            body.AngularVelocity = 1f;
            body.Rotation = 0.5f;
            world.Step(0.5f);

            Assert.IsTrue(body.IsValid);
            Assert.AreEqual(new Vector2(2f, 2f), body.Position);
            Assert.AreEqual(1f, body.Rotation, 1e-6f);
            Assert.AreEqual(new Vector2(2f, 0f), body.State.Velocity);
            Assert.IsTrue(body.IsKinematic);
            Assert.AreEqual(3f, body.Mass);

            body.AddForce(new Vector2(4f, 0f));
            body.AddImpulse(new Vector2(0f, 5f));
            body.AddForceAtPoint(new Vector2(1f, 0f), new Vector2(0f, 2f));
            body.AddImpulseAtPoint(new Vector2(0f, 1f), new Vector2(3f, 0f));
            body.AddTorque(1.5f);
            body.AddTorqueImpulse(0.5f);

            Assert.AreEqual(new Vector2(5f, 6f), world.Pushed);
            Assert.AreEqual(new Vector2(3f, 2f), world.Points);
            Assert.AreEqual(2f, world.Turned);

            world.RemoveBody(handle);

            Assert.IsFalse(body.IsValid);
            Assert.IsFalse(default(PhysicsBody2D).IsValid);
        }

        [Test]
        public void APhysicsBodyTellsTheHandleOfItsBody()
        {
            var world = new PointWorld();
            BodyHandle handle = world.CreateBody(new BodyDesc(BodyKind.Dynamic, BodyShape.Sphere(1f), Vector3.Zero, Quaternion.Identity, 1f));

            Assert.AreEqual(handle, new PhysicsBody(world, handle).Handle);
            Assert.AreEqual(new BodyHandle(7), new PhysicsBody2D(null, new BodyHandle(7)).Handle);
            Assert.IsFalse(default(PhysicsBody).Handle.IsValid);
        }

        [Test]
        public void TwoDimensionalShapesKeepTheirMeasures()
        {
            BodyShape2D box = BodyShape2D.Box(new Vector2(1f, 2f));
            BodyShape2D circle = BodyShape2D.Circle(0.5f);
            BodyShape2D capsule = BodyShape2D.Capsule(0.25f, 1f);

            Assert.AreEqual((ShapeKind2D.Box, new Vector2(1f, 2f)), (box.Kind, box.HalfExtents));
            Assert.AreEqual((ShapeKind2D.Circle, 0.5f), (circle.Kind, circle.Radius));
            Assert.AreEqual((ShapeKind2D.Capsule, 0.25f, 1f), (capsule.Kind, capsule.Radius, capsule.HalfHeight));
        }

        [Test]
        public void LocalPeerCarriesThePhysicsBackendAndAMatchingClientStarts()
        {
            var rig = new Rig(PhysicsBackend.Rapier);
            Peer client = rig.Connect(PhysicsBackend.Rapier);

            Assert.AreEqual(ConnectionState.Started, client.Session.State);
            Assert.AreNotEqual(0UL, client.Objects.LocalPeerId);
        }

        [Test]
        public void AClientWithAnotherPhysicsBackendStopsWithPhysicsBackendMismatch()
        {
            var rig = new Rig(PhysicsBackend.Rapier);
            Peer client = rig.Connect(PhysicsBackend.Rigidbody);

            Assert.AreEqual(ConnectionState.Stopped, client.Session.State);
            Assert.AreEqual(StopReason.PhysicsBackendMismatch, client.StopReason);
            Assert.IsEmpty(client.Mismatches);
        }

        [Test]
        public void LocalPeerWithAnUnknownPhysicsBackendStopsTheClientWithObjectMismatch()
        {
            var rig = new Rig(PhysicsBackend.Rigidbody);
            Peer client = rig.Connect(PhysicsBackend.Rigidbody);

            rig.Server.Send(rig.StartedPeers[0], TestObjects.LocalPeerId, LocalPeerNetAdapter.Instance.Encode(new LocalPeer { PeerId = rig.StartedPeers[0], TickRate = 30, PhysicsBackend = 0 }).Span);
            rig.Run(5);

            Assert.AreEqual(ObjectMismatchKind.InvalidMessage, client.Mismatches[0].Kind);
            Assert.AreEqual(StopReason.ObjectMismatch, client.StopReason);
        }

        [Test]
        public void TheDefaultPhysicsBackendIsRigidbodyOnBothSides()
        {
            var rig = new Rig(null);
            Peer client = rig.Connect(null);

            Assert.AreEqual(PhysicsBackend.Rigidbody, rig.Objects.PhysicsBackend);
            Assert.AreEqual(PhysicsBackend.Rigidbody, client.Objects.PhysicsBackend);
            Assert.AreEqual(ConnectionState.Started, client.Session.State);
        }

        private sealed class Rig
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly ServerSession Server;
            public readonly ServerObjects Objects;
            public readonly List<Peer> Clients = new List<Peer>();
            public readonly List<ulong> StartedPeers = new List<ulong>();
            public TimeSpan Now = TimeSpan.FromSeconds(1);

            public Rig(PhysicsBackend? backend)
            {
                Server = new ServerSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestObjects.Schema()), TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ServerObjects(Server, TestObjects.Protocol(TestObjects.Channels()), objectId => default);
                if (backend.HasValue)
                {
                    Objects.PhysicsBackend = backend.Value;
                }

                Server.OnRemoteConnectionState += args =>
                {
                    if (args.State == ConnectionState.Started)
                    {
                        StartedPeers.Add(args.PeerId);
                    }
                };
                Server.Start(Listener);
            }

            public Peer Connect(PhysicsBackend? backend)
            {
                var peer = new Peer(this, backend);
                Clients.Add(peer);
                Run(10);
                return peer;
            }

            public void Run(int steps)
            {
                for (int step = 0; step < steps; step++)
                {
                    Now += TimeSpan.FromMilliseconds(16);
                    Server.Tick(Now);
                    foreach (Peer client in Clients)
                    {
                        client.Session.Tick(Now);
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
            public readonly List<ObjectMismatchArgs> Mismatches = new List<ObjectMismatchArgs>();

            public Peer(Rig rig, PhysicsBackend? backend)
            {
                Session = new ClientSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestObjects.Schema()), TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ClientObjects(Session, TestObjects.Protocol(TestObjects.Channels()), new NoSpawner());
                if (backend.HasValue)
                {
                    Objects.PhysicsBackend = backend.Value;
                }

                Objects.OnObjectMismatch += Mismatches.Add;
                Session.OnClientConnectionState += args =>
                {
                    if (args.State == ConnectionState.Stopped)
                    {
                        StopReason = args.Reason;
                    }
                };
                Session.Start(rig.Listener.Connect(), rig.Now);
            }

            public StopReason StopReason { get; private set; }
        }

        private sealed class NoSpawner : IObjectSpawner
        {
            public SpawnResult Spawn(in SpawnedObject spawned) => SpawnResult.Spawned;

            public void Despawn(uint objectId)
            {
            }
        }

        private sealed class PointWorld : IPhysicsWorld
        {
            private readonly Dictionary<int, BodyState> bodies = new Dictionary<int, BodyState>();
            private int nextBody = 1;

            public int SnapshotsCreated { get; private set; }

            public PhysicsBackend Backend => PhysicsBackend.Rigidbody;

            public BodyHandle CreateBody(in BodyDesc desc)
            {
                var body = new BodyHandle(nextBody++);
                bodies.Add(body.Value, new BodyState { Position = desc.Position, Rotation = desc.Rotation });
                return body;
            }

            public bool RemoveBody(BodyHandle body) => bodies.Remove(body.Value);

            public bool Contains(BodyHandle body) => bodies.ContainsKey(body.Value);

            public BodyState GetBody(BodyHandle body) => bodies[body.Value];

            public BodyKind GetKind(BodyHandle body) => BodyKind.Dynamic;

            public float GetMass(BodyHandle body) => 1f;

            public void SetBody(BodyHandle body, in BodyState state) => bodies[body.Value] = state;

            public void SetRewindable(BodyHandle body, bool rewindable)
            {
            }

            public void AddForce(BodyHandle body, Vector3 force)
            {
            }

            public void AddImpulse(BodyHandle body, Vector3 impulse)
            {
            }

            public Vector3 Turned { get; private set; }

            public Vector3 PushedAtPoint { get; private set; }

            public Vector3 Points { get; private set; }

            public void AddTorque(BodyHandle body, Vector3 torque) => Turned += torque;

            public void AddTorqueImpulse(BodyHandle body, Vector3 torqueImpulse) => Turned += torqueImpulse;

            public void AddForceAtPoint(BodyHandle body, Vector3 force, Vector3 point)
            {
                PushedAtPoint += force;
                Points += point;
            }

            public void AddImpulseAtPoint(BodyHandle body, Vector3 impulse, Vector3 point)
            {
                PushedAtPoint += impulse;
                Points += point;
            }

            public void Step(float seconds)
            {
                foreach (int key in new List<int>(bodies.Keys))
                {
                    BodyState state = bodies[key];
                    state.Position += state.Velocity * seconds;
                    bodies[key] = state;
                }
            }

            public PhysicsSnapshot CreateSnapshot()
            {
                SnapshotsCreated++;
                return new PointSnapshot();
            }

            public void Save(PhysicsSnapshot into)
            {
                var snapshot = (PointSnapshot)into;
                snapshot.Bodies.Clear();
                foreach (KeyValuePair<int, BodyState> body in bodies)
                {
                    snapshot.Bodies.Add(body.Key, body.Value);
                }
            }

            public void Load(PhysicsSnapshot from)
            {
                foreach (KeyValuePair<int, BodyState> body in ((PointSnapshot)from).Bodies)
                {
                    if (bodies.ContainsKey(body.Key))
                    {
                        bodies[body.Key] = body.Value;
                    }
                }
            }

            public QueryFilter LastFilter { get; private set; } = new QueryFilter(0, new BodyHandle(-1), false);

            public BodyShape LastShape { get; private set; }

            public Vector3 LastPosition { get; private set; }

            public Quaternion LastRotation { get; private set; }

            public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, in QueryFilter filter, out RayHit hit)
            {
                LastFilter = filter;
                hit = default;
                return false;
            }

            public bool ShapeCast(in BodyShape shape, Vector3 position, Quaternion rotation, Vector3 direction, float maxDistance, in QueryFilter filter, out RayHit hit)
            {
                hit = default;
                return false;
            }

            public int Overlap(in BodyShape shape, Vector3 position, Quaternion rotation, in QueryFilter filter, BodyHandle[] results)
            {
                LastShape = shape;
                LastPosition = position;
                LastRotation = rotation;
                LastFilter = filter;
                return 0;
            }

            public int GetContactPoints(BodyHandle body, BodyHandle other, in QueryFilter filter, List<ContactPointInfo> into)
            {
                LastFilter = filter;
                into.Clear();
                into.Add(new ContactPointInfo(new Vector3(body.Value, other.Value, 0f), Vector3.UnitY, 0.25f));
                return 1;
            }

            private sealed class PointSnapshot : PhysicsSnapshot
            {
                public readonly Dictionary<int, BodyState> Bodies = new Dictionary<int, BodyState>();
            }
        }

        private sealed class PointWorld2D : IPhysicsWorld2D
        {
            private readonly Dictionary<int, BodyState2D> bodies = new Dictionary<int, BodyState2D>();
            private readonly Dictionary<int, (BodyKind Kind, float Mass)> traits = new Dictionary<int, (BodyKind Kind, float Mass)>();
            private int nextBody = 1;

            public Vector2 Pushed { get; private set; }

            public PhysicsBackend Backend => PhysicsBackend.Rigidbody;

            public BodyHandle CreateBody(in BodyDesc2D desc)
            {
                var body = new BodyHandle(nextBody++);
                bodies.Add(body.Value, new BodyState2D { Position = desc.Position, Rotation = desc.Rotation });
                traits.Add(body.Value, (desc.Kind, desc.Mass));
                return body;
            }

            public bool RemoveBody(BodyHandle body) => bodies.Remove(body.Value) && traits.Remove(body.Value);

            public bool Contains(BodyHandle body) => bodies.ContainsKey(body.Value);

            public BodyState2D GetBody(BodyHandle body) => bodies[body.Value];

            public BodyKind GetKind(BodyHandle body) => traits[body.Value].Kind;

            public float GetMass(BodyHandle body) => traits[body.Value].Mass;

            public void SetBody(BodyHandle body, in BodyState2D state) => bodies[body.Value] = state;

            public void SetRewindable(BodyHandle body, bool rewindable)
            {
            }

            public void AddForce(BodyHandle body, Vector2 force) => Pushed += force;

            public void AddImpulse(BodyHandle body, Vector2 impulse) => Pushed += impulse;

            public float Turned { get; private set; }

            public Vector2 Points { get; private set; }

            public void AddTorque(BodyHandle body, float torque) => Turned += torque;

            public void AddTorqueImpulse(BodyHandle body, float torqueImpulse) => Turned += torqueImpulse;

            public void AddForceAtPoint(BodyHandle body, Vector2 force, Vector2 point)
            {
                Pushed += force;
                Points += point;
            }

            public void AddImpulseAtPoint(BodyHandle body, Vector2 impulse, Vector2 point)
            {
                Pushed += impulse;
                Points += point;
            }

            public void Step(float seconds)
            {
                foreach (int key in new List<int>(bodies.Keys))
                {
                    BodyState2D state = bodies[key];
                    state.Position += state.Velocity * seconds;
                    state.Rotation += state.AngularVelocity * seconds;
                    bodies[key] = state;
                }
            }

            public PhysicsSnapshot CreateSnapshot() => new PointSnapshot2D();

            public void Save(PhysicsSnapshot into)
            {
                var snapshot = (PointSnapshot2D)into;
                snapshot.Bodies.Clear();
                foreach (KeyValuePair<int, BodyState2D> body in bodies)
                {
                    snapshot.Bodies.Add(body.Key, body.Value);
                }
            }

            public void Load(PhysicsSnapshot from)
            {
                foreach (KeyValuePair<int, BodyState2D> body in ((PointSnapshot2D)from).Bodies)
                {
                    if (bodies.ContainsKey(body.Key))
                    {
                        bodies[body.Key] = body.Value;
                    }
                }
            }

            public QueryFilter LastFilter { get; private set; } = new QueryFilter(0, new BodyHandle(-1), false);

            public BodyShape2D LastShape { get; private set; }

            public Vector2 LastPosition { get; private set; }

            public float LastRotation { get; private set; } = float.NaN;

            public bool Raycast(Vector2 origin, Vector2 direction, float maxDistance, in QueryFilter filter, out RayHit2D hit)
            {
                LastFilter = filter;
                hit = default;
                return false;
            }

            public bool ShapeCast(in BodyShape2D shape, Vector2 position, float rotation, Vector2 direction, float maxDistance, in QueryFilter filter, out RayHit2D hit)
            {
                hit = default;
                return false;
            }

            public int Overlap(in BodyShape2D shape, Vector2 position, float rotation, in QueryFilter filter, BodyHandle[] results)
            {
                LastShape = shape;
                LastPosition = position;
                LastRotation = rotation;
                LastFilter = filter;
                return 0;
            }

            public int GetContactPoints(BodyHandle body, BodyHandle other, in QueryFilter filter, List<ContactPointInfo2D> into)
            {
                LastFilter = filter;
                into.Clear();
                into.Add(new ContactPointInfo2D(new Vector2(body.Value, other.Value), Vector2.UnitY, 0.25f));
                return 1;
            }

            private sealed class PointSnapshot2D : PhysicsSnapshot
            {
                public readonly Dictionary<int, BodyState2D> Bodies = new Dictionary<int, BodyState2D>();
            }
        }
    }
}
