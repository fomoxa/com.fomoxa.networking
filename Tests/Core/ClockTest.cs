using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ClockTest
    {
        private const int TickRate = 30;
        private static readonly TimeSpan Step = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / TickRate);

        [Test]
        public void TheEstimatorWaitsForATickRateAndASample()
        {
            var estimator = new ClockEstimator(new ClockSettings());

            Assert.IsFalse(estimator.AddSample(900, 500, ClockProtocol.NoInputLead, Millis(1100)));
            estimator.Advance(Millis(1100));

            Assert.IsFalse(estimator.Synced);
            Assert.AreEqual(0u, estimator.ServerTick);
            Assert.AreEqual(0u, estimator.PredictionTick);
            Assert.AreEqual(TimeSpan.Zero, estimator.Rtt);
            Assert.AreEqual(1.0, estimator.TickScale);
        }

        [Test]
        public void TheFirstSampleGivesTheRttTheServerTickAndALeadOfHalfTheRttPlusTheInputBuffer()
        {
            ClockEstimator estimator = Synced(out TimeSpan now);

            Assert.IsTrue(estimator.Synced);
            Assert.AreEqual(200, estimator.Rtt.TotalMilliseconds, 1e-6);
            Assert.AreEqual(503u, estimator.ServerTick);
            Assert.AreEqual(508u, estimator.PredictionTick);
            Assert.AreEqual(1.0, estimator.TickScale);
            Assert.IsFalse(estimator.HasInputLead);
        }

        [Test]
        public void ThePredictionTickAdvancesOnePerTickAtNormalSpeedWhenOnTarget()
        {
            ClockEstimator estimator = Synced(out TimeSpan now);

            for (int tick = 1; tick <= 30; tick++)
            {
                estimator.Advance(now + Ticks(tick));

                Assert.AreEqual(508u + (uint)tick, estimator.PredictionTick);
                Assert.AreEqual(503u + (uint)tick, estimator.ServerTick);
                Assert.AreEqual(1.0, estimator.TickScale, 1e-3);
            }
        }

        [Test]
        public void AClientAheadOfTheTargetSlowsDownWithinTheCap()
        {
            ClockEstimator estimator = Synced(out TimeSpan now);

            estimator.Advance(now);
            Assert.AreEqual(1.05, estimator.TickScale, 1e-3);
            estimator.Advance(now);
            Assert.AreEqual(1.10, estimator.TickScale, 1e-3);
            estimator.Advance(now);
            Assert.AreEqual(1.10, estimator.TickScale, 1e-3);
            Assert.AreEqual(511u, estimator.PredictionTick);
        }

        [Test]
        public void AClientBehindTheTargetSpeedsUpWithinTheCap()
        {
            ClockEstimator estimator = Synced(out TimeSpan now);

            estimator.Advance(now + Ticks(2));
            Assert.AreEqual(0.95, estimator.TickScale, 1e-3);
            estimator.Advance(now + Ticks(5));
            Assert.AreEqual(0.90, estimator.TickScale, 1e-3);
        }

        [Test]
        public void AnErrorBeyondTheThresholdResetsThePredictionTick()
        {
            ClockEstimator estimator = Synced(out TimeSpan now);

            estimator.Advance(now + Ticks(15));

            Assert.AreEqual(523u, estimator.PredictionTick);
            Assert.AreEqual(518u, estimator.ServerTick);
            Assert.AreEqual(1.0, estimator.TickScale);
        }

        [Test]
        public void TheRttIsSmoothedWithAWeightOfOneEighth()
        {
            ClockEstimator estimator = Synced(out TimeSpan now);

            Assert.IsTrue(estimator.AddSample(ClockEstimator.ClientTimeOf(now), 503, ClockProtocol.NoInputLead, now + TimeSpan.FromMilliseconds(280)));

            Assert.AreEqual(210, estimator.Rtt.TotalMilliseconds, 1e-6);
        }

        [Test]
        public void ASampleOverTwiceTheRttIsSkippedUntilThreeInARow()
        {
            ClockEstimator estimator = Synced(out TimeSpan now);
            uint sent = ClockEstimator.ClientTimeOf(now);

            for (int rejected = 0; rejected < ClockEstimator.OutliersBeforeAccepting; rejected++)
            {
                Assert.IsFalse(estimator.AddSample(sent, 503, ClockProtocol.NoInputLead, now + TimeSpan.FromMilliseconds(1000)));
                Assert.AreEqual(200, estimator.Rtt.TotalMilliseconds, 1e-6);
            }

            Assert.IsTrue(estimator.AddSample(sent, 503, ClockProtocol.NoInputLead, now + TimeSpan.FromMilliseconds(1000)));
            Assert.AreEqual(300, estimator.Rtt.TotalMilliseconds, 1e-6);
        }

        [Test]
        public void TheClientTimeMayWrap()
        {
            var estimator = new ClockEstimator(new ClockSettings());
            estimator.SetTickRate(TickRate);
            TimeSpan now = TimeSpan.FromMilliseconds(4294967296.0 + 150);

            Assert.IsTrue(estimator.AddSample(uint.MaxValue - 49, 500, ClockProtocol.NoInputLead, now));

            Assert.AreEqual(200, estimator.Rtt.TotalMilliseconds, 1e-6);
        }

        [Test]
        public void AnInputLeadBelowTheInputBufferRaisesTheLead()
        {
            ClockEstimator estimator = Synced(out TimeSpan now);

            Assert.IsTrue(estimator.AddSample(ClockEstimator.ClientTimeOf(now) - 200, 503, 0, now));
            estimator.Advance(now + Ticks(1));

            Assert.IsTrue(estimator.HasInputLead);
            Assert.AreEqual(0.90, estimator.TickScale, 1e-3);
        }

        [Test]
        public void AnotherTickRateOrAResetStartsOver()
        {
            ClockEstimator estimator = Synced(out TimeSpan now);

            estimator.SetTickRate(60);

            Assert.IsFalse(estimator.Synced);
            Assert.AreEqual(60, estimator.TickRate);
            Assert.AreEqual(0u, estimator.PredictionTick);

            estimator.Reset();

            Assert.AreEqual(0, estimator.TickRate);
            Assert.Throws<ArgumentOutOfRangeException>(() => estimator.SetTickRate(0));
        }

        [Test]
        public void TheClientSyncsWithTheServerOverLoopbackAndTheServerLearnsTheReportedRtt()
        {
            var rig = new Rig();
            Peer client = rig.Connect();
            rig.Run(10);

            ClockEstimator estimator = client.Clock.Estimator;
            Assert.AreEqual(TickRate, estimator.TickRate);
            Assert.IsTrue(estimator.Synced);
            Assert.AreEqual(2 * Step.TotalMilliseconds, estimator.Rtt.TotalMilliseconds, 1.0);
            Assert.AreEqual(rig.ServerTick, estimator.ServerTick, 1.0);
            Assert.AreEqual(rig.ServerTick + 4, estimator.PredictionTick, 1.0);
            Assert.IsTrue(rig.Clock.HasRtt(client.PeerId));
            Assert.AreEqual(estimator.Rtt.TotalMilliseconds, rig.Clock.RttOf(client.PeerId).TotalMilliseconds, 1.0);
        }

        [Test]
        public void TheClientSendsThreePingsAtStartThenOnePerInterval()
        {
            var pings = new List<TickPing>();
            var rig = new Rig(onPing: pings.Add);
            Peer client = rig.Connect();
            rig.Run(10);

            Assert.AreEqual(ClientClock.StartupPings, pings.Count);
            Assert.AreEqual(0, pings[0].Rtt);

            rig.Run(TickRate + 5);

            Assert.AreEqual(ClientClock.StartupPings + 1, pings.Count);
            Assert.IsFalse(client.Clock.Estimator.Synced);
        }

        [Test]
        public void APeerThatStopsIsForgottenAndAStoppedClientStartsOver()
        {
            var rig = new Rig();
            Peer client = rig.Connect();
            rig.Run(10);
            ulong peerId = client.PeerId;

            client.Session.Stop();
            rig.Run(5);

            Assert.IsFalse(rig.Clock.HasRtt(peerId));
            Assert.AreEqual(TimeSpan.Zero, rig.Clock.RttOf(peerId));
            Assert.IsFalse(client.Clock.Estimator.Synced);
            Assert.AreEqual(0, client.Clock.Estimator.TickRate);
            Assert.AreEqual(0, client.Objects.ServerTickRate);
        }

        [Test]
        public void AStoppedServerForgetsEveryRtt()
        {
            var rig = new Rig();
            Peer client = rig.Connect();
            rig.Run(10);

            rig.Server.Stop();

            Assert.IsFalse(rig.Clock.HasRtt(client.PeerId));
        }

        [Test]
        public void LocalPeerCarriesTheServerTickRate()
        {
            var rig = new Rig();
            rig.Objects.TickRate = 60;
            Peer client = rig.Connect();
            rig.Run(10);

            Assert.AreEqual(60, client.Objects.ServerTickRate);
            Assert.AreEqual(60, client.Clock.Estimator.TickRate);
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Objects.TickRate = 0);
        }

        [Test]
        public void LocalPeerWithATickRateOfZeroStopsTheClientWithObjectMismatch()
        {
            var rig = new Rig();
            Peer client = rig.Connect();
            rig.Run(10);

            rig.Server.Send(client.PeerId, TestObjects.LocalPeerId, LocalPeerNetAdapter.Instance.Encode(new LocalPeer { PeerId = client.PeerId, TickRate = 0 }).Span);
            rig.Run(5);

            Assert.AreEqual(1, client.Mismatches.Count);
            Assert.AreEqual(ObjectMismatchKind.InvalidMessage, client.Mismatches[0].Kind);
            Assert.AreEqual(StopReason.ObjectMismatch, client.StopReason);
        }

        private static ClockEstimator Synced(out TimeSpan now)
        {
            var estimator = new ClockEstimator(new ClockSettings());
            estimator.SetTickRate(TickRate);
            now = Millis(1100);
            Assert.IsTrue(estimator.AddSample(900, 500, ClockProtocol.NoInputLead, now));
            estimator.Advance(now);
            return estimator;
        }

        private static TimeSpan Millis(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

        private static TimeSpan Ticks(int count) => TimeSpan.FromTicks(TimeSpan.TicksPerSecond * count / TickRate);

        private sealed class Rig
        {
            public readonly LoopbackListener Listener = new LoopbackListener(64);
            public readonly ServerSession Server;
            public readonly ServerObjects Objects;
            public readonly ServerClock Clock;
            public readonly List<Peer> Clients = new List<Peer>();
            public readonly List<ulong> StartedPeers = new List<ulong>();
            public TimeSpan Now = TimeSpan.FromSeconds(1);
            public uint ServerTick;

            public Rig(Action<TickPing> onPing = null)
            {
                var dispatcher = new MessageDispatcher(TestObjects.Schema());
                Server = new ServerSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), dispatcher, TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ServerObjects(Server, TestObjects.Protocol(TestObjects.Channels()), objectId => default);
                if (onPing == null)
                {
                    Clock = new ServerClock(Server, TestObjects.ClockProtocol());
                }
                else
                {
                    var ping = new TickPing();
                    dispatcher.Register(TestObjects.TickPingId, (peerId, payload) =>
                    {
                        TickPingNetAdapter.Instance.Decode(payload, ref ping);
                        onPing(new TickPing { ClientTime = ping.ClientTime, Rtt = ping.Rtt });
                    });
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

            public Peer Connect()
            {
                var peer = new Peer(this);
                Clients.Add(peer);
                return peer;
            }

            public void Run(int steps)
            {
                for (int step = 0; step < steps; step++)
                {
                    Now += Step;
                    ServerTick++;
                    if (Clock != null)
                    {
                        Clock.Tick = ServerTick;
                    }

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
            public readonly List<ObjectMismatchArgs> Mismatches = new List<ObjectMismatchArgs>();
            private readonly Rig rig;
            private readonly int index;

            public Peer(Rig rig)
            {
                this.rig = rig;
                index = rig.Clients.Count;
                Session = new ClientSession(
                    TestObjects.Schema(),
                    new SessionConfig(),
                    new SessionLimits(),
                    new MessageDispatcher(TestObjects.Schema()),
                    TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ClientObjects(Session, TestObjects.Protocol(TestObjects.Channels()), new NoSpawner());
                Clock = new ClientClock(Session, TestObjects.ClockProtocol(), new ClockSettings());
                Objects.OnLocalPeerAssigned += peerId => Clock.Estimator.SetTickRate(Objects.ServerTickRate);
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

            public ulong PeerId => rig.StartedPeers[index];
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
