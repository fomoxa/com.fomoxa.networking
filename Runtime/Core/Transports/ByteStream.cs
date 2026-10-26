using System;
using System.Net.Sockets;

namespace Fomoxa.Networking.Transports
{
    internal interface IByteStream : IDisposable
    {
        int Send(ReadOnlySpan<byte> bytes, out SocketError error);

        int Receive(Span<byte> buffer, out SocketError error);
    }

    internal sealed class SocketByteStream : IByteStream
    {
        private readonly Socket socket;

        public SocketByteStream(Socket socket)
        {
            this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
            socket.Blocking = false;
            socket.NoDelay = true;
        }

        public int Send(ReadOnlySpan<byte> bytes, out SocketError error)
        {
            if (!socket.Poll(0, SelectMode.SelectWrite))
            {
                error = SocketError.WouldBlock;
                return 0;
            }

            return socket.Send(bytes, SocketFlags.None, out error);
        }

        public int Receive(Span<byte> buffer, out SocketError error) => socket.Receive(buffer, SocketFlags.None, out error);

        public void Dispose() => socket.Dispose();
    }
}
