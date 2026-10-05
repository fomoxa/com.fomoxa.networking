using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Sessions;
using NUnit.Framework;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class ObjectMessageTest
    {
        private const uint ObjectCallId = 0x2000_0002;
        private const double FrameSeconds = 1.0 / 30;

        private NetworkManager host;
        private TimeSpan now;

        [SetUp]
        public void CreateHost()
        {
            host = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            host.Registry = TestObjects.Registry(new MessageSchema(ObjectCallId, 0xF00E, new ulong[] { 0xF00E }));
            host.Initialize();
            now = TimeSpan.Zero;
        }

        [TearDown]
        public void DestroyHost()
        {
            host.ClientManager.StopConnection();
            host.ServerManager.StopConnection();
            UnityEngine.Object.DestroyImmediate(host.gameObject);
        }

        private void RunFrames(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                host.RunFrameStart(FrameSeconds, now);
                host.RunFrameEnd();
            }
        }

        [Test]
        public void HostClientAndServerExchangeObjectMessages()
        {
            var serverCalls = new List<(uint ObjectId, byte BehaviourIndex, byte Value)>();
            var clientCalls = new List<(uint ObjectId, byte BehaviourIndex, byte Value)>();
            host.ServerManager.Dispatcher.RegisterObject(ObjectCallId, (peerId, objectId, behaviourIndex, body) => serverCalls.Add((objectId, behaviourIndex, body.Span[0])));
            host.ClientManager.Dispatcher.RegisterObject(ObjectCallId, (peerId, objectId, behaviourIndex, body) => clientCalls.Add((objectId, behaviourIndex, body.Span[0])));
            ulong peerId = 0;
            host.ServerManager.OnRemoteConnectionState += args => peerId = args.PeerId;
            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);

            Assert.AreEqual(SendResult.Queued, host.ClientManager.SendToObject(ObjectCallId, 7, 1, new byte[] { 11 }));
            Assert.AreEqual(SendResult.Queued, host.ServerManager.SendToObject(peerId, ObjectCallId, 8, 2, new byte[] { 12 }));
            RunFrames(3);

            Assert.AreEqual(new[] { (7u, (byte)1, (byte)11) }, serverCalls.ToArray());
            Assert.AreEqual(new[] { (8u, (byte)2, (byte)12) }, clientCalls.ToArray());
        }
    }
}
