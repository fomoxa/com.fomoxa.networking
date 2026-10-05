using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Tests
{
    public sealed class ManagerObjectsTest
    {
        private const double FrameSeconds = 1.0 / 30;

        private NetworkManager host;
        private TimeSpan now;

        [SetUp]
        public void CreateHost()
        {
            host = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            host.Registry = TestObjects.Registry();
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

        [Test]
        public void UnregisteredPrefabStopsTheClientWithPrefabMismatchAndLogsAnError()
        {
            var states = new List<ConnectionStateArgs>();
            host.ClientManager.OnClientConnectionState += states.Add;
            Connect();

            host.ServerManager.Send(1, TestObjects.SpawnId, TestObjects.Spawn(1, 0xA1, 0));
            LogAssert.Expect(LogType.Error, new Regex("object mismatch UnknownPrefab, object 1, prefab 0x000000A1"));
            RunFrames(3);

            Assert.AreEqual(ConnectionState.Stopped, host.ClientManager.State);
            Assert.AreEqual(StopReason.PrefabMismatch, states[states.Count - 1].Reason);
            Assert.IsFalse(states[states.Count - 1].WillRetry);
        }

        [Test]
        public void ObjectMismatchHandlerReplacesTheLog()
        {
            var mismatches = new List<ObjectMismatchArgs>();
            host.ClientManager.OnObjectMismatch += mismatches.Add;
            Connect();

            host.ServerManager.Send(1, TestObjects.DespawnId, TestObjects.Despawn(9));
            RunFrames(3);

            Assert.AreEqual(1, mismatches.Count);
            Assert.AreEqual(ObjectMismatchKind.UnknownObject, mismatches[0].Kind);
            Assert.AreEqual(9u, mismatches[0].ObjectId);
            Assert.AreEqual(ConnectionState.Stopped, host.ClientManager.State);
        }

        private void Connect()
        {
            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
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
    }
}
