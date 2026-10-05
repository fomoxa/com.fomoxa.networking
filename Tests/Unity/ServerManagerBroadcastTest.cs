using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Sessions;
using NUnit.Framework;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class ServerManagerBroadcastTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const double FrameSeconds = 1.0 / 30;

        private NetworkManager host;

        [SetUp]
        public void CreateHost()
        {
            host = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            host.Registry = TestObjects.Registry(new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }));
            host.Initialize();
        }

        [TearDown]
        public void DestroyHost()
        {
            host.ClientManager.StopConnection();
            host.ServerManager.StopConnection();
            UnityEngine.Object.DestroyImmediate(host.gameObject);
        }

        [Test]
        public void BroadcastReachesTheHostClient()
        {
            var received = new List<byte>();
            host.ClientManager.Dispatcher.Register(GreetingId, (peerId, payload) => received.Add(payload.Span[0]));
            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            TimeSpan now = TimeSpan.Zero;
            for (int frame = 0; frame < 20; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                host.RunFrameStart(FrameSeconds, now);
                host.RunFrameEnd();
            }

            Assert.AreEqual(1, host.ServerManager.Broadcast(GreetingId, new byte[] { 9 }));
            for (int frame = 0; frame < 3; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                host.RunFrameStart(FrameSeconds, now);
                host.RunFrameEnd();
            }

            Assert.AreEqual(new byte[] { 9 }, received.ToArray());
            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
        }
    }
}
