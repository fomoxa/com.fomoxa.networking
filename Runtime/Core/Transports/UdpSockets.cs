using System;
using System.Net;
using System.Net.Sockets;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    internal enum UdpReceiveStatus
    {
        Received,
        WouldBlock,
        Discard,
        Closed,
        Error,
    }

    internal static class UdpSockets
    {
        private const int SioUdpConnectionReset = -1744830452;

        public static Socket Create(AddressFamily family)
        {
            var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
            socket.Blocking = false;
            SilenceConnectionReset(socket);
            return socket;
        }

        public static IPAddress AnyAddress(AddressFamily family) =>
            family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;

        public static SendOutcome SendTo(Socket socket, byte[] bytes, int length, IPEndPoint address)
        {
            try
            {
                socket.SendTo(bytes, 0, length, SocketFlags.None, address);
                return SendOutcome.Ok;
            }
            catch (SocketException error)
            {
                switch (error.SocketErrorCode)
                {
                    case SocketError.WouldBlock:
                    case SocketError.NoBufferSpaceAvailable:
                        return SendOutcome.WouldBlock;
                    case SocketError.MessageSize:
                        return SendOutcome.TooLarge;
                    case SocketError.ConnectionReset:
                        return SendOutcome.Ok;
                    default:
                        return SendOutcome.Error;
                }
            }
            catch (ObjectDisposedException)
            {
                return SendOutcome.Closed;
            }
        }

        public static UdpReceiveStatus ReceiveFrom(Socket socket, byte[] buffer, out int count, out EndPoint from)
        {
            from = new IPEndPoint(AnyAddress(socket.AddressFamily), 0);
            count = 0;
            try
            {
                count = socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref from);
                return UdpReceiveStatus.Received;
            }
            catch (SocketException error)
            {
                switch (error.SocketErrorCode)
                {
                    case SocketError.WouldBlock:
                        return UdpReceiveStatus.WouldBlock;
                    case SocketError.ConnectionReset:
                    case SocketError.MessageSize:
                        return UdpReceiveStatus.Discard;
                    default:
                        return UdpReceiveStatus.Error;
                }
            }
            catch (ObjectDisposedException)
            {
                return UdpReceiveStatus.Closed;
            }
        }

        private static void SilenceConnectionReset(Socket socket)
        {
            try
            {
                socket.IOControl(SioUdpConnectionReset, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (SocketException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
