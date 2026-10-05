using System;
using System.Collections.Generic;
using System.Linq;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class BrowserWebSocketTest
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private FakeBrowserSocket bridge;
        private TimeSpan now;

        [SetUp]
        public void CreateBridge()
        {
            bridge = new FakeBrowserSocket();
            now = TimeSpan.Zero;
        }

        [TearDown]
        public void ResetPlatform()
        {
            NetworkTransport.BrowserOverride = null;
            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            created.Clear();
        }

        [Test]
        public void TheConnectorOpensTheUrlAndConnectsWhenTheSocketOpens()
        {
            ITransportConnector connector = Factory().CreateConnector("game.example", 7777);

            Assert.AreEqual(ConnectStatus.Pending, connector.Poll(out ITransport transport));
            Assert.AreEqual("ws://game.example:7777/play", bridge.Url);
            Assert.AreEqual(ConnectStatus.Pending, connector.Poll(out transport));

            bridge.StateNow = BrowserSocketState.Open;

            Assert.AreEqual(ConnectStatus.Connected, connector.Poll(out transport));
            Assert.AreEqual(TransportKind.Message, transport.Kind);
            Assert.AreEqual(ConnectStatus.Failed, connector.Poll(out _));
            Assert.AreEqual(0, bridge.Released);
        }

        [Test]
        public void TheBrowserTakesWssUrlsAndAFailedOrSlowSocketFailsTheConnector()
        {
            BrowserWebSocketFactory factory = Factory();
            ITransportConnector secure = factory.CreateConnector("wss://game.example/fomoxa", 1);
            secure.Poll(out _);
            Assert.AreEqual("wss://game.example/fomoxa", bridge.Url);

            bridge.StateNow = BrowserSocketState.Failed;
            Assert.AreEqual(ConnectStatus.Failed, secure.Poll(out _));
            Assert.AreEqual(1, bridge.Released);

            bridge.StateNow = BrowserSocketState.Connecting;
            ITransportConnector slow = factory.CreateConnector("127.0.0.1", 9000);
            slow.Poll(out _);
            now = TimeSpan.FromSeconds(6);
            Assert.AreEqual(ConnectStatus.Failed, slow.Poll(out _));
            Assert.AreEqual(2, bridge.Released);
        }

        [Test]
        public void SendCopiesTheMessageAndWaitsWhileTheBrowserBufferIsOverTheLimit()
        {
            ITransport transport = Connected();

            Assert.AreEqual(TransportSignal.Ok, transport.Send(new byte[] { 1, 2, 3 }).Signal);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, bridge.Sent.Single());

            bridge.Buffered = 100;
            Assert.AreEqual(TransportSignal.WouldBlock, transport.Send(new byte[] { 4 }).Signal);
            bridge.Buffered = 99;
            Assert.AreEqual(TransportSignal.Ok, transport.Send(new byte[] { 4 }).Signal);
            Assert.AreEqual(TransportSignal.TooLarge, transport.Send(new byte[FomoxaWire.MaxDataFrameSize + 1]).Signal);
        }

        [Test]
        public void ReceiveTakesOneMessageAskingForCapacityAndDrainsBeforeReportingClosed()
        {
            ITransport transport = Connected();
            var buffer = new byte[4];

            Assert.AreEqual(TransportSignal.WouldBlock, transport.Receive(buffer).Signal);

            bridge.Queue.Enqueue(new byte[] { 9, 8, 7, 6, 5, 4 });
            bridge.Queue.Enqueue(new byte[] { 1 });
            bridge.StateNow = BrowserSocketState.Closed;
            ReceiveOutcome tooSmall = transport.Receive(buffer);
            Assert.AreEqual(TransportSignal.NeedCapacity, tooSmall.Signal);
            Assert.AreEqual(6, tooSmall.Count);

            var larger = new byte[6];
            Assert.AreEqual(6, transport.Receive(larger).Count);
            CollectionAssert.AreEqual(new byte[] { 9, 8, 7, 6, 5, 4 }, larger);
            Assert.AreEqual(1, transport.Receive(buffer).Count);
            Assert.AreEqual(TransportSignal.Closed, transport.Receive(buffer).Signal);
            Assert.AreEqual(TransportSignal.Closed, transport.Send(new byte[1]).Signal);
        }

        [Test]
        public void AFailedSocketIsAnErrorAndCloseAndDisposeReachTheBrowser()
        {
            ITransport transport = Connected();

            transport.CloseGracefully();
            Assert.AreEqual(1, bridge.Closed);

            bridge.StateNow = BrowserSocketState.Failed;
            Assert.AreEqual(TransportSignal.Error, transport.Receive(new byte[4]).Signal);
            Assert.AreEqual(TransportSignal.Error, transport.Send(new byte[1]).Signal);

            transport.Dispose();
            transport.Dispose();
            Assert.AreEqual(1, bridge.Released);
            Assert.AreEqual(TransportSignal.Closed, transport.Receive(new byte[4]).Signal);
        }

        [Test]
        public void ABrowserCannotListenOrUseTcpOrUdp()
        {
            Assert.Throws<PlatformNotSupportedException>(() => Factory().CreateListener(0, out _));

            NetworkTransport.BrowserOverride = true;
            var tcp = Component<TcpNetworkTransport>();
            var udp = Component<UdpNetworkTransport>();
            StringAssert.Contains("WebSocketNetworkTransport", Assert.Throws<PlatformNotSupportedException>(() => tcp.CreateFactory()).Message);
            Assert.Throws<PlatformNotSupportedException>(() => udp.CreateFactory());
            Assert.IsInstanceOf<BrowserWebSocketFactory>(Component<WebSocketNetworkTransport>().CreateFactory());

            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            created.Add(manager.gameObject);
            manager.Registry = TestObjects.Registry();
            Assert.Throws<PlatformNotSupportedException>(() => manager.Initialize());
        }

        [Test]
        public void AServerCannotStartInABrowser()
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            created.Add(manager.gameObject);
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = Component<WebSocketNetworkTransport>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();

            NetworkTransport.BrowserOverride = true;

            Assert.Throws<PlatformNotSupportedException>(() => manager.ServerManager.StartConnection(0));
        }

        private T Component<T>()
            where T : NetworkTransport
        {
            var component = new GameObject(typeof(T).Name).AddComponent<T>();
            created.Add(component.gameObject);
            return component;
        }

        private BrowserWebSocketFactory Factory() =>
            new BrowserWebSocketFactory(new WebSocketSettings { Path = "/play" }, 100, bridge, () => now);

        private ITransport Connected()
        {
            ITransportConnector connector = Factory().CreateConnector("127.0.0.1", 9000);
            bridge.StateNow = BrowserSocketState.Open;
            Assert.AreEqual(ConnectStatus.Connected, connector.Poll(out ITransport transport));
            return transport;
        }

        private sealed class FakeBrowserSocket : IBrowserSocket
        {
            public readonly Queue<byte[]> Queue = new Queue<byte[]>();

            public readonly List<byte[]> Sent = new List<byte[]>();

            public string Url { get; private set; }

            public BrowserSocketState StateNow { get; set; }

            public int Buffered { get; set; }

            public int Closed { get; private set; }

            public int Released { get; private set; }

            public int Open(string url)
            {
                Url = url;
                return 1;
            }

            public BrowserSocketState State(int id) => StateNow;

            public int BufferedAmount(int id) => Buffered;

            public void Send(int id, byte[] data, int length) => Sent.Add(data.Take(length).ToArray());

            public int NextLength(int id) => Queue.Count > 0 ? Queue.Peek().Length : -1;

            public int Receive(int id, byte[] buffer, int capacity)
            {
                byte[] message = Queue.Dequeue();
                Array.Copy(message, buffer, message.Length);
                return message.Length;
            }

            public void Close(int id) => Closed++;

            public void Release(int id) => Released++;
        }
    }
}
