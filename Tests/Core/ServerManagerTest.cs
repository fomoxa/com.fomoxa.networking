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
    public sealed class ServerManagerTest
    {
        [Test]
        public void StartingListensOnTheTransportAndALocalLoopbackAndStoppingClearsThem()
        {
            var rig = new Rig();

            rig.Server.StartConnection(0);

            Assert.AreEqual(ServerState.Started, rig.Server.State);
            Assert.AreEqual(Factory.BoundPort, rig.Server.Port);
            Assert.IsNotNull(rig.Server.LocalListener);
            Assert.Throws<InvalidOperationException>(() => rig.Server.StartConnection(0));

            rig.Server.StopConnection();

            Assert.AreEqual(ServerState.Stopped, rig.Server.State);
            Assert.AreEqual(0, rig.Server.Port);
            Assert.IsNull(rig.Server.LocalListener);
        }

        [Test]
        public void StartingSpawnsTheSceneObjectsTheHostPresents()
        {
            var rig = new Rig();
            var door = new ServerSceneContentTest.SceneEntity(0, 7);
            rig.Host.Present.Add(door);

            rig.Server.StartConnection(0);

            Assert.IsNotNull(door.Record);
            Assert.AreSame(door, rig.Server.Spawned[door.Record.ObjectId]);
        }

        [Test]
        public void TheObserverRuleIsTheRuleOfTheServerTable()
        {
            var rig = new Rig();
            var rule = new DistanceObserverRule(5);

            rig.Server.ObserverRule = rule;

            Assert.AreSame(rule, rig.Server.ObserverRule);
            Assert.AreSame(rule, rig.Server.Entities.ObserverRule);
        }

        [Test]
        public void LoadingASceneTheHostDoesNotKnowIsRefused()
        {
            var rig = new Rig();
            rig.Host.Unknown.Add(9);
            rig.Server.StartConnection(0);

            Assert.Throws<ArgumentException>(() => rig.Server.Scenes.LoadGlobal(new uint[] { 9 }));
        }

        [Test]
        public void SceneZeroIsRefusedAsNoNetworkSceneWithoutAskingTheHost()
        {
            var rig = new Rig();
            rig.Host.Unknown.Add(0);
            rig.Server.StartConnection(0);

            ArgumentException global = Assert.Throws<ArgumentException>(() => rig.Server.Scenes.LoadGlobal(new uint[] { 0 }));
            ArgumentException perPeer = Assert.Throws<ArgumentException>(() => rig.Server.Scenes.LoadForPeers(0, new ulong[0]));

            StringAssert.Contains("scene id 0 means no network scene", global.Message);
            StringAssert.Contains("scene id 0 means no network scene", perPeer.Message);
        }

        private sealed class Rig
        {
            public readonly ServerSceneContentTest.FakeHost Host = new ServerSceneContentTest.FakeHost();
            public readonly ServerManager Server;

            public Rig()
            {
                MessageChannels channels = TestObjects.Channels();
                Server = new ServerManager(
                    TestObjects.Schema(),
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
                    new Factory(),
                    8,
                    new ServerSceneContentTest.SceneBackend(Host),
                    Host,
                    new NetworkLog(exception => throw exception, message => { }));
            }
        }

        private sealed class Factory : ITransportFactory
        {
            public const ushort BoundPort = 7777;

            public int FrameBudget => FomoxaWire.MaxDataFrameSize;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                boundPort = BoundPort;
                return new LoopbackListener(8);
            }

            public ITransportConnector CreateConnector(string address, ushort port) => throw new NotSupportedException();
        }
    }
}
