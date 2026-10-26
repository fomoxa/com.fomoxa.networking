using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Org.BouncyCastle.Crypto.Parameters;

namespace Fomoxa.Networking.Transports
{
    internal sealed class EncryptedUdpTransport : ITransport
    {
        private readonly Socket socket;
        private readonly IPEndPoint peer;
        private readonly ulong sessionId;
        private readonly byte[] token;
        private readonly byte[] attempt = new byte[EncryptedDatagram.AttemptSize];
        private readonly KeyParameter sendKey;
        private readonly KeyParameter receiveKey;
        private readonly DatagramCipher cipher = new DatagramCipher();
        private readonly byte[] datagram = new byte[EncryptedDatagram.MaxDatagram];
        private readonly byte[] plain = new byte[EncryptedDatagram.MaxDatagram];
        private readonly byte[] opened = new byte[EncryptedDatagram.MaxDatagram];
        private ReplayWindow window;
        private ulong nextSequence;
        private int openedLength = -1;
        private bool established;
        private bool released;

        public EncryptedUdpTransport(Socket socket, IPEndPoint peer, ulong sessionId, ConnectionGrant grant)
        {
            this.socket = socket;
            this.peer = peer;
            this.sessionId = sessionId;
            token = grant.Token.ToArray();
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(attempt);
            }

            sendKey = new KeyParameter(DatagramCipher.DeriveClientKey(grant.ClientKey.ToArray(), attempt));
            receiveKey = new KeyParameter(DatagramCipher.DeriveServerKey(grant.ServerKey.ToArray(), attempt));
        }

        public TransportKind Kind => TransportKind.Message;

        public SendOutcome Send(ReadOnlySpan<byte> bytes)
        {
            if (released)
            {
                return SendOutcome.Closed;
            }

            int headerSize = established ? EncryptedDatagram.DataHeaderSize : EncryptedDatagram.ConnectHeaderSize(token.Length);
            if (headerSize + bytes.Length + DatagramCipher.TagLength > EncryptedDatagram.MaxDatagram)
            {
                return SendOutcome.TooLarge;
            }

            ulong sequence = nextSequence++;
            if (established)
            {
                EncryptedDatagram.WriteDataHeader(datagram, sessionId, EncryptedDatagram.DataKind, sequence);
            }
            else
            {
                EncryptedDatagram.WriteConnectHeader(datagram, sessionId, sequence, token, attempt);
            }

            bytes.CopyTo(plain);
            int sealedLength = cipher.Seal(sendKey, sequence, datagram, headerSize, plain, 0, bytes.Length, datagram, headerSize);
            return UdpSockets.SendTo(socket, datagram, headerSize + sealedLength, peer);
        }

        public ReceiveOutcome Receive(Span<byte> buffer)
        {
            if (released)
            {
                return ReceiveOutcome.Closed;
            }

            while (openedLength < 0)
            {
                UdpReceiveStatus status = UdpSockets.ReceiveFrom(socket, datagram, out int count, out EndPoint from);
                if (status == UdpReceiveStatus.WouldBlock)
                {
                    return ReceiveOutcome.WouldBlock;
                }

                if (status == UdpReceiveStatus.Closed)
                {
                    return ReceiveOutcome.Closed;
                }

                if (status == UdpReceiveStatus.Error)
                {
                    return ReceiveOutcome.Error;
                }

                if (status == UdpReceiveStatus.Received && peer.Equals(from))
                {
                    TryOpen(count);
                }
            }

            if (openedLength > buffer.Length)
            {
                return ReceiveOutcome.NeedCapacity(openedLength);
            }

            new ReadOnlySpan<byte>(opened, 0, openedLength).CopyTo(buffer);
            int delivered = openedLength;
            openedLength = -1;
            return ReceiveOutcome.Ok(delivered);
        }

        public void CloseGracefully()
        {
        }

        public void Dispose()
        {
            if (released)
            {
                return;
            }

            released = true;
            socket.Dispose();
        }

        private void TryOpen(int count)
        {
            if (count < EncryptedDatagram.DataOverhead
                || datagram[EncryptedDatagram.KindOffset] != EncryptedDatagram.DataKind
                || EncryptedDatagram.ReadSessionId(datagram) != sessionId)
            {
                return;
            }

            ulong sequence = EncryptedDatagram.ReadSequence(datagram);
            if (!window.IsFresh(sequence)
                || !cipher.TryOpen(receiveKey, sequence, datagram, EncryptedDatagram.DataHeaderSize, datagram, EncryptedDatagram.DataHeaderSize, count - EncryptedDatagram.DataHeaderSize, opened, 0, out int length))
            {
                return;
            }

            window.Mark(sequence);
            established = true;
            openedLength = length;
        }
    }
}
