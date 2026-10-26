using System;
using System.Net.Sockets;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    internal sealed class TlsTcpTransport : ITransport
    {
        private readonly TlsChannel channel;
        private bool errored;
        private bool peerClosed;
        private bool released;

        public TlsTcpTransport(TlsChannel channel)
        {
            this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        public TransportKind Kind => TransportKind.Stream;

        public SendOutcome Send(ReadOnlySpan<byte> bytes)
        {
            if (released)
            {
                return SendOutcome.Closed;
            }

            if (errored)
            {
                return SendOutcome.Error;
            }

            int sent = channel.Send(bytes, out SocketError error);
            if (error != SocketError.Success && error != SocketError.WouldBlock)
            {
                errored = true;
                return SendOutcome.Error;
            }

            return sent < bytes.Length || error == SocketError.WouldBlock ? SendOutcome.WouldBlock : SendOutcome.Ok;
        }

        public ReceiveOutcome Receive(Span<byte> buffer)
        {
            if (released || peerClosed)
            {
                return ReceiveOutcome.Closed;
            }

            if (errored)
            {
                return ReceiveOutcome.Error;
            }

            if (buffer.Length == 0)
            {
                return ReceiveOutcome.WouldBlock;
            }

            int read = channel.Receive(buffer, out SocketError error);
            if (error == SocketError.WouldBlock)
            {
                return ReceiveOutcome.WouldBlock;
            }

            if (error != SocketError.Success)
            {
                errored = true;
                return ReceiveOutcome.Error;
            }

            if (read == 0)
            {
                peerClosed = true;
                return ReceiveOutcome.Closed;
            }

            return ReceiveOutcome.Ok(read);
        }

        public void CloseGracefully()
        {
            if (released || errored)
            {
                return;
            }

            channel.Shutdown();
        }

        public void Dispose()
        {
            if (released)
            {
                return;
            }

            released = true;
            channel.Dispose();
        }
    }
}
