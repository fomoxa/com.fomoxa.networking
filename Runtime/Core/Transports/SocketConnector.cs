using System;
using System.Net;
using System.Net.Sockets;

namespace Fomoxa.Networking.Transports
{
    internal sealed class SocketConnector : IDisposable
    {
        private readonly IHostResolver resolver;
        private readonly ushort port;
        private IPAddress[] addresses;
        private int nextAddress;
        private Socket socket;
        private bool finished;

        public SocketConnector(IHostResolver resolver, ushort port)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.port = port;
        }

        public ConnectStatus Poll(out Socket connected)
        {
            connected = null;
            if (finished)
            {
                return ConnectStatus.Failed;
            }

            if (addresses == null)
            {
                ResolveStatus resolved = resolver.Poll(out addresses);
                if (resolved == ResolveStatus.Pending)
                {
                    return ConnectStatus.Pending;
                }

                if (resolved == ResolveStatus.Failed)
                {
                    return Fail();
                }
            }

            return socket == null ? ConnectNext(out connected) : PollConnecting(out connected);
        }

        public void Dispose()
        {
            finished = true;
            socket?.Dispose();
            socket = null;
        }

        private ConnectStatus ConnectNext(out Socket connected)
        {
            connected = null;
            if (nextAddress == addresses.Length)
            {
                return Fail();
            }

            var remote = new IPEndPoint(addresses[nextAddress++], port);
            try
            {
                socket = new Socket(remote.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
                socket.Connect(remote);
            }
            catch (SocketException error) when (IsInProgress(error.SocketErrorCode))
            {
                return ConnectStatus.Pending;
            }
            catch (SocketException)
            {
                return RetryWithNextAddress(out connected);
            }

            return Complete(out connected);
        }

        private ConnectStatus PollConnecting(out Socket connected)
        {
            connected = null;
            if (socket.Poll(0, SelectMode.SelectError))
            {
                return RetryWithNextAddress(out connected);
            }

            if (!socket.Poll(0, SelectMode.SelectWrite))
            {
                return ConnectStatus.Pending;
            }

            if ((int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error) != 0)
            {
                return RetryWithNextAddress(out connected);
            }

            return Complete(out connected);
        }

        private ConnectStatus RetryWithNextAddress(out Socket connected)
        {
            socket?.Dispose();
            socket = null;
            return ConnectNext(out connected);
        }

        private ConnectStatus Complete(out Socket connected)
        {
            connected = socket;
            socket = null;
            finished = true;
            return ConnectStatus.Connected;
        }

        private ConnectStatus Fail()
        {
            Dispose();
            return ConnectStatus.Failed;
        }

        private static bool IsInProgress(SocketError error) =>
            error == SocketError.WouldBlock
            || error == SocketError.InProgress
            || error == SocketError.AlreadyInProgress;
    }
}
