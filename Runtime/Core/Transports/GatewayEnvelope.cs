using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Fomoxa.Networking.Transports
{
    internal static class GatewayEnvelope
    {
        public const int Length = 20;
        public const byte Version = 1;

        private const byte IPv4 = 4;
        private const byte IPv6 = 6;
        private const byte WebSocket = 0x80;
        private const int PortOffset = 2;
        private const int AddressOffset = 4;
        private const int IPv4Length = 4;
        private const int IPv6Length = 16;

        public static void Write(byte[] buffer, IPEndPoint client, bool overWebSocket)
        {
            Array.Clear(buffer, 0, Length);
            buffer[0] = Version;
            BinaryPrimitives.WriteUInt16BigEndian(new Span<byte>(buffer, PortOffset, 2), (ushort)client.Port);
            byte[] address = client.Address.GetAddressBytes();
            byte family = client.AddressFamily == AddressFamily.InterNetwork ? IPv4 : IPv6;
            buffer[1] = overWebSocket ? (byte)(family | WebSocket) : family;
            Buffer.BlockCopy(address, 0, buffer, AddressOffset, address.Length);
        }

        public static bool TryRead(byte[] buffer, int count, out IPEndPoint client, out bool overWebSocket)
        {
            client = null;
            overWebSocket = false;
            if (count < Length || buffer[0] != Version)
            {
                return false;
            }

            int port = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(buffer, PortOffset, 2));
            byte[] address;
            switch (buffer[1] & ~WebSocket)
            {
                case IPv4:
                    for (int index = AddressOffset + IPv4Length; index < Length; index++)
                    {
                        if (buffer[index] != 0)
                        {
                            return false;
                        }
                    }

                    address = new byte[IPv4Length];
                    break;
                case IPv6:
                    address = new byte[IPv6Length];
                    break;
                default:
                    return false;
            }

            Buffer.BlockCopy(buffer, AddressOffset, address, 0, address.Length);
            client = new IPEndPoint(new IPAddress(address), port);
            overWebSocket = (buffer[1] & WebSocket) != 0;
            return true;
        }
    }
}
