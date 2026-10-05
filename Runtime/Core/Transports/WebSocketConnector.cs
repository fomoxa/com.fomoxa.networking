using System;
using System.Net.Sockets;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    internal sealed class WebSocketConnector : ITransportConnector
    {
        private readonly SocketConnector connector;
        private readonly WebSocketSettings settings;
        private readonly string key;
        private readonly byte[] request;
        private readonly byte[] response;
        private Socket socket;
        private TimeSpan started;
        private int requestSent;
        private int used;
        private bool finished;

        public WebSocketConnector(IHostResolver resolver, ushort port, string target, string host, WebSocketSettings settings)
        {
            connector = new SocketConnector(resolver, port);
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            key = WebSocketHandshake.NewKey();
            request = WebSocketHandshake.Request(target, host, key);
            response = new byte[settings.MaxHandshakeBytes];
        }

        public ConnectStatus Poll(out ITransport transport)
        {
            transport = null;
            if (finished)
            {
                return ConnectStatus.Failed;
            }

            if (socket == null)
            {
                ConnectStatus connected = connector.Poll(out socket);
                if (connected != ConnectStatus.Connected)
                {
                    return connected == ConnectStatus.Failed ? Fail() : ConnectStatus.Pending;
                }

                started = settings.Clock();
            }

            if (settings.Clock() - started > settings.HandshakeTimeout)
            {
                return Fail();
            }

            if (!WriteRequest())
            {
                return finished ? ConnectStatus.Failed : ConnectStatus.Pending;
            }

            return ReadResponse(out transport);
        }

        public void Dispose()
        {
            finished = true;
            connector.Dispose();
            socket?.Dispose();
            socket = null;
        }

        private bool WriteRequest()
        {
            while (requestSent < request.Length)
            {
                int sent = socket.Send(new ReadOnlySpan<byte>(request, requestSent, request.Length - requestSent), SocketFlags.None, out SocketError error);
                if (error == SocketError.WouldBlock || (error == SocketError.Success && sent <= 0))
                {
                    return false;
                }

                if (error != SocketError.Success)
                {
                    Fail();
                    return false;
                }

                requestSent += sent;
            }

            return true;
        }

        private ConnectStatus ReadResponse(out ITransport transport)
        {
            transport = null;
            int read = socket.Receive(new Span<byte>(response, used, response.Length - used), SocketFlags.None, out SocketError error);
            if (error == SocketError.WouldBlock)
            {
                return ConnectStatus.Pending;
            }

            if (error != SocketError.Success || read == 0)
            {
                return Fail();
            }

            used += read;
            int headerEnd = WebSocketHandshake.HeaderEnd(response, used);
            if (headerEnd < 0)
            {
                return used == response.Length ? Fail() : ConnectStatus.Pending;
            }

            if (!WebSocketHandshake.AcceptsResponse(response, headerEnd, key))
            {
                return Fail();
            }

            transport = new WebSocketTransport(socket, true, new ReadOnlySpan<byte>(response, headerEnd, used - headerEnd));
            socket = null;
            finished = true;
            return ConnectStatus.Connected;
        }

        private ConnectStatus Fail()
        {
            Dispose();
            return ConnectStatus.Failed;
        }
    }
}
