using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    internal sealed class WebSocketListener : IListenerTransport
    {
        internal const int MaxPendingHandshakes = 128;

        private readonly Socket socket;
        private readonly WebSocketSettings settings;
        private readonly List<PendingHandshake> pending = new List<PendingHandshake>();
        private bool released;

        public WebSocketListener(IPEndPoint local, WebSocketSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            socket = new Socket(local.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(local);
            socket.Listen(MaxPendingHandshakes);
            socket.Blocking = false;
        }

        public IPEndPoint LocalEndPoint => (IPEndPoint)socket.LocalEndPoint;

        internal int PendingCount => pending.Count;

        public AcceptOutcome Accept()
        {
            if (released)
            {
                return AcceptOutcome.Error;
            }

            bool progressed = AcceptSocket();
            TimeSpan now = settings.Clock();
            for (int index = 0; index < pending.Count; index++)
            {
                PendingHandshake handshake = pending[index];
                HandshakeStatus status = handshake.Advance(now, settings);
                if (status == HandshakeStatus.Pending)
                {
                    continue;
                }

                pending.RemoveAt(index);
                if (status == HandshakeStatus.Accepted)
                {
                    return AcceptOutcome.Accepted(handshake.Upgrade());
                }

                handshake.Dispose();
                index--;
                progressed = true;
            }

            return progressed ? AcceptOutcome.Progress : AcceptOutcome.Pending;
        }

        public void Dispose()
        {
            if (released)
            {
                return;
            }

            released = true;
            foreach (PendingHandshake handshake in pending)
            {
                handshake.Dispose();
            }

            pending.Clear();
            socket.Dispose();
        }

        private bool AcceptSocket()
        {
            if (pending.Count >= MaxPendingHandshakes)
            {
                return false;
            }

            try
            {
                Socket peer = socket.Accept();
                peer.Blocking = false;
                pending.Add(new PendingHandshake(peer, settings.Clock(), settings.MaxHandshakeBytes));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private enum HandshakeStatus
        {
            Pending,
            Accepted,
            Failed,
        }

        private sealed class PendingHandshake : IDisposable
        {
            private readonly Socket socket;
            private readonly TimeSpan started;
            private readonly byte[] request;
            private int used;
            private int headerEnd;
            private byte[] response;
            private int responseSent;
            private bool accepted;
            private bool draining;

            public PendingHandshake(Socket socket, TimeSpan started, int maxHandshakeBytes)
            {
                this.socket = socket;
                this.started = started;
                request = new byte[maxHandshakeBytes];
            }

            public HandshakeStatus Advance(TimeSpan now, WebSocketSettings settings)
            {
                if (now - started > settings.HandshakeTimeout)
                {
                    return HandshakeStatus.Failed;
                }

                if (draining)
                {
                    return Drain();
                }

                if (response == null && !ReadRequest(settings))
                {
                    return headerEnd < 0 ? HandshakeStatus.Failed : HandshakeStatus.Pending;
                }

                return WriteResponse();
            }

            public ITransport Upgrade() =>
                new WebSocketTransport(socket, false, new ReadOnlySpan<byte>(request, headerEnd, used - headerEnd));

            public void Dispose() => socket.Dispose();

            private bool ReadRequest(WebSocketSettings settings)
            {
                int read = socket.Receive(new Span<byte>(request, used, request.Length - used), SocketFlags.None, out SocketError error);
                if (error == SocketError.WouldBlock)
                {
                    return false;
                }

                if (error != SocketError.Success || read == 0)
                {
                    headerEnd = -1;
                    return false;
                }

                used += read;
                headerEnd = WebSocketHandshake.HeaderEnd(request, used);
                if (headerEnd < 0)
                {
                    if (used == request.Length)
                    {
                        response = WebSocketHandshake.Status("431 Request Header Fields Too Large");
                        return true;
                    }

                    headerEnd = 0;
                    return false;
                }

                response = WebSocketHandshake.Respond(request, headerEnd, settings, out accepted);
                return true;
            }

            private HandshakeStatus WriteResponse()
            {
                while (responseSent < response.Length)
                {
                    int sent = socket.Send(new ReadOnlySpan<byte>(response, responseSent, response.Length - responseSent), SocketFlags.None, out SocketError error);
                    if (error == SocketError.WouldBlock || (error == SocketError.Success && sent <= 0))
                    {
                        return HandshakeStatus.Pending;
                    }

                    if (error != SocketError.Success)
                    {
                        return HandshakeStatus.Failed;
                    }

                    responseSent += sent;
                }

                if (accepted)
                {
                    return HandshakeStatus.Accepted;
                }

                try
                {
                    socket.Shutdown(SocketShutdown.Send);
                }
                catch (SocketException)
                {
                    return HandshakeStatus.Failed;
                }

                draining = true;
                return Drain();
            }

            private HandshakeStatus Drain()
            {
                while (true)
                {
                    int read = socket.Receive(request, 0, request.Length, SocketFlags.None, out SocketError error);
                    if (error == SocketError.WouldBlock)
                    {
                        return HandshakeStatus.Pending;
                    }

                    if (error != SocketError.Success || read == 0)
                    {
                        return HandshakeStatus.Failed;
                    }
                }
            }
        }
    }
}
