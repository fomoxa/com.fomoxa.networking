using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Sessions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class NetworkStatsDisplayTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private readonly List<GameObject> created = new List<GameObject>();
        private TimeSpan now;

        [SetUp]
        public void ResetClock()
        {
            now = TimeSpan.Zero;
        }

        [TearDown]
        public void DestroyEverything()
        {
            foreach (NetworkManager manager in managers)
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
            }

            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            managers.Clear();
            created.Clear();
        }

        private NetworkManager Manager(bool collectStatistics = false)
        {
            var gameObject = new GameObject("NetworkManager");
            created.Add(gameObject);
            NetworkManager manager = gameObject.AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("collectStatistics").boolValue = collectStatistics;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry(new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }));
            manager.Initialize();
            managers.Add(manager);
            return manager;
        }

        private NetworkManager Host(bool collectStatistics = false)
        {
            NetworkManager host = Manager(collectStatistics);
            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(host, 20);
            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
            return host;
        }

        private NetworkStatsDisplay Display(GameObject on, NetworkManager manager = null, Action<SerializedObject> configure = null)
        {
            NetworkStatsDisplay display = on.AddComponent<NetworkStatsDisplay>();
            var serialized = new SerializedObject(display);
            serialized.FindProperty("manager").objectReferenceValue = manager;
            configure?.Invoke(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return display;
        }

        private GameObject Empty()
        {
            var gameObject = new GameObject("Display");
            created.Add(gameObject);
            return gameObject;
        }

        private void RunFrames(NetworkManager manager, int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                manager.RunFrameStart(FrameSeconds, now);
                manager.RunFrameEnd();
            }
        }

        private static void ShowEverything(SerializedObject serialized)
        {
            serialized.FindProperty("showConnection").boolValue = true;
            serialized.FindProperty("showTraffic").boolValue = true;
            serialized.FindProperty("showTime").boolValue = true;
            serialized.FindProperty("showPrediction").boolValue = true;
        }

        private static string[] Lines(NetworkStatsDisplay display) => display.Text.Split('\n');

        private static double Rate(string line, string unit) =>
            double.Parse(Regex.Match(line, @"([0-9.]+) " + Regex.Escape(unit)).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        [Test]
        public void WithoutAManagerItSaysSo()
        {
            NetworkStatsDisplay display = Display(Empty());

            display.Refresh(0f);

            Assert.AreEqual("NetworkStatsDisplay has no NetworkManager", display.Text);
        }

        [Test]
        public void AnEmptyManagerFieldUsesTheManagerOnTheSameGameObject()
        {
            NetworkManager host = Host();
            NetworkStatsDisplay display = Display(host.gameObject);

            display.Refresh(0f);

            StringAssert.StartsWith("Client Started, RTT ", display.Text);
        }

        [Test]
        public void ByDefaultOnlyTheConnectionAndTimeGroupsAreShown()
        {
            NetworkManager host = Host();
            NetworkStatsDisplay display = Display(Empty(), host);

            display.Refresh(0f);

            string[] lines = Lines(display);
            Assert.AreEqual(3, lines.Length, display.Text);
            StringAssert.StartsWith("Client Started, RTT ", lines[0]);
            Assert.AreEqual("Server Started, 1 peers", lines[1]);
            StringAssert.StartsWith("Tick ", lines[2]);
            StringAssert.Contains(", clock synced", lines[2]);
        }

        [Test]
        public void AStoppedManagerShowsOneConnectionLine()
        {
            NetworkManager manager = Manager();
            NetworkStatsDisplay display = Display(Empty(), manager, serialized => serialized.FindProperty("showTime").boolValue = false);

            display.Refresh(0f);

            Assert.AreEqual("Client and server stopped", display.Text);
        }

        [Test]
        public void TrafficWithoutCollectStatisticsShowsAReminderForEachRole()
        {
            NetworkManager host = Host();
            NetworkStatsDisplay display = Display(Empty(), host, ShowEverything);

            display.Refresh(0f);

            StringAssert.Contains("Client traffic: turn on Collect Statistics on the NetworkManager", display.Text);
            StringAssert.Contains("Server traffic: turn on Collect Statistics on the NetworkManager", display.Text);
        }

        [Test]
        public void TrafficRatesAreTheChangeOfTheCountersOverTheInterval()
        {
            NetworkManager host = Host(collectStatistics: true);
            host.ServerManager.Dispatcher.Register(GreetingId, (peerId, payload) => { });
            NetworkStatsDisplay display = Display(Empty(), host, ShowEverything);
            display.Refresh(10f);
            long sentBefore = host.ClientManager.Statistics.Sent.Bundles.Count;
            for (int frame = 0; frame < 30; frame++)
            {
                host.ClientManager.Send(GreetingId, new byte[] { 1 });
                RunFrames(host, 1);
            }

            long sent = host.ClientManager.Statistics.Sent.Bundles.Count - sentBefore;

            display.Refresh(11f);

            string clientSent = Array.Find(Lines(display), line => line.StartsWith("Client sent "));
            Assert.IsNotNull(clientSent, display.Text);
            Assert.Greater(sent, 0);
            Assert.AreEqual(sent, Rate(clientSent, "bundles/s"));
            StringAssert.Contains(" resent/s", clientSent);
            Assert.IsNotNull(Array.Find(Lines(display), line => line.StartsWith("Client received ") && line.EndsWith(" duplicates/s")), display.Text);
            Assert.IsNotNull(Array.Find(Lines(display), line => line.StartsWith("Server sent ")), display.Text);
            Assert.IsNotNull(Array.Find(Lines(display), line => line.StartsWith("Server received ")), display.Text);
        }

        [Test]
        public void TheFirstRefreshShowsZeroRates()
        {
            NetworkManager host = Host(collectStatistics: true);
            NetworkStatsDisplay display = Display(Empty(), host, ShowEverything);

            display.Refresh(5f);

            string clientSent = Array.Find(Lines(display), line => line.StartsWith("Client sent "));
            Assert.AreEqual("Client sent 0 bundles/s, 0.0 KiB/s, 0 messages/s, 0 resent/s", clientSent);
        }

        [Test]
        public void ThePredictionGroupShowsReconcilesOnTheClientAndDroppedInputsOnTheServer()
        {
            NetworkManager host = Host();
            NetworkStatsDisplay display = Display(Empty(), host, ShowEverything);

            display.Refresh(0f);

            CollectionAssert.Contains(Lines(display), "Reconciles 0, 0.0/s");
            CollectionAssert.Contains(Lines(display), "Dropped inputs 0");
        }
    }
}
