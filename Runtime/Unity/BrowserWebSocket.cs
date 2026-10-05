using System;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Unity
{
    internal enum BrowserSocketState
    {
        Connecting = 0,
        Open = 1,
        Closing = 2,
        Closed = 3,
        Failed = 4,
    }

    internal interface IBrowserSocket
    {
        int Open(string url);

        BrowserSocketState State(int id);

        int BufferedAmount(int id);

        void Send(int id, byte[] data, int length);

        int NextLength(int id);

        int Receive(int id, byte[] buffer, int capacity);

        void Close(int id);

        void Release(int id);
    }

    internal sealed class BrowserWebSocketFactory : ITransportFactory
    {
        private readonly WebSocketSettings settings;
        private readonly int maxBufferedBytes;
        private readonly IBrowserSocket bridge;
        private readonly Func<TimeSpan> clock;

        public BrowserWebSocketFactory(WebSocketSettings settings, int maxBufferedBytes, IBrowserSocket bridge, Func<TimeSpan> clock)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.maxBufferedBytes = maxBufferedBytes > 0 ? maxBufferedBytes : throw new ArgumentOutOfRangeException(nameof(maxBufferedBytes), "the browser send buffer limit is positive");
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public int FrameBudget => FomoxaWire.MaxDataFrameSize;

        public IListenerTransport CreateListener(ushort port, out ushort boundPort) =>
            throw new PlatformNotSupportedException("a browser cannot listen for connections; WebGL builds are clients only");

        public ITransportConnector CreateConnector(string address, ushort port) =>
            new BrowserWebSocketConnector(bridge, WebSocketTransportFactory.UrlFor(address, port, settings.Path), settings.HandshakeTimeout, maxBufferedBytes, clock);
    }

    internal sealed class BrowserWebSocketConnector : ITransportConnector
    {
        private readonly IBrowserSocket bridge;
        private readonly string url;
        private readonly TimeSpan timeout;
        private readonly int maxBufferedBytes;
        private readonly Func<TimeSpan> clock;
        private TimeSpan started;
        private int id;
        private bool finished;

        public BrowserWebSocketConnector(IBrowserSocket bridge, string url, TimeSpan timeout, int maxBufferedBytes, Func<TimeSpan> clock)
        {
            this.bridge = bridge;
            this.url = url;
            this.timeout = timeout;
            this.maxBufferedBytes = maxBufferedBytes;
            this.clock = clock;
        }

        public ConnectStatus Poll(out ITransport transport)
        {
            transport = null;
            if (finished)
            {
                return ConnectStatus.Failed;
            }

            if (id == 0)
            {
                id = bridge.Open(url);
                started = clock();
            }

            switch (bridge.State(id))
            {
                case BrowserSocketState.Open:
                    transport = new BrowserWebSocketTransport(bridge, id, maxBufferedBytes);
                    id = 0;
                    finished = true;
                    return ConnectStatus.Connected;
                case BrowserSocketState.Connecting when clock() - started <= timeout:
                    return ConnectStatus.Pending;
                default:
                    Dispose();
                    return ConnectStatus.Failed;
            }
        }

        public void Dispose()
        {
            finished = true;
            if (id != 0)
            {
                bridge.Release(id);
                id = 0;
            }
        }
    }

    internal sealed class BrowserWebSocketTransport : ITransport
    {
        private readonly IBrowserSocket bridge;
        private readonly int id;
        private readonly int maxBufferedBytes;
        private byte[] scratch = new byte[4096];
        private bool released;

        public BrowserWebSocketTransport(IBrowserSocket bridge, int id, int maxBufferedBytes)
        {
            this.bridge = bridge;
            this.id = id;
            this.maxBufferedBytes = maxBufferedBytes;
        }

        public TransportKind Kind => TransportKind.Message;

        public SendOutcome Send(ReadOnlySpan<byte> bytes)
        {
            if (released)
            {
                return SendOutcome.Closed;
            }

            BrowserSocketState state = bridge.State(id);
            if (state == BrowserSocketState.Failed)
            {
                return SendOutcome.Error;
            }

            if (state != BrowserSocketState.Open)
            {
                return SendOutcome.Closed;
            }

            if (bytes.Length > FomoxaWire.MaxDataFrameSize)
            {
                return SendOutcome.TooLarge;
            }

            if (bridge.BufferedAmount(id) >= maxBufferedBytes)
            {
                return SendOutcome.WouldBlock;
            }

            EnsureScratch(bytes.Length);
            bytes.CopyTo(scratch);
            bridge.Send(id, scratch, bytes.Length);
            return SendOutcome.Ok;
        }

        public ReceiveOutcome Receive(Span<byte> buffer)
        {
            if (released)
            {
                return ReceiveOutcome.Closed;
            }

            int next = bridge.NextLength(id);
            if (next >= 0)
            {
                if (next > buffer.Length)
                {
                    return ReceiveOutcome.NeedCapacity(next);
                }

                EnsureScratch(next);
                int length = bridge.Receive(id, scratch, scratch.Length);
                new ReadOnlySpan<byte>(scratch, 0, length).CopyTo(buffer);
                return ReceiveOutcome.Ok(length);
            }

            switch (bridge.State(id))
            {
                case BrowserSocketState.Failed:
                    return ReceiveOutcome.Error;
                case BrowserSocketState.Closed:
                    return ReceiveOutcome.Closed;
                default:
                    return ReceiveOutcome.WouldBlock;
            }
        }

        public void CloseGracefully()
        {
            if (!released)
            {
                bridge.Close(id);
            }
        }

        public void Dispose()
        {
            if (released)
            {
                return;
            }

            released = true;
            bridge.Release(id);
        }

        private void EnsureScratch(int length)
        {
            if (scratch.Length < length)
            {
                scratch = new byte[Math.Max(length, scratch.Length * 2)];
            }
        }
    }
}
