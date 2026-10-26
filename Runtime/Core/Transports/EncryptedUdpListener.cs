using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Fomoxa.Net.Transports;
using Org.BouncyCastle.Crypto.Parameters;

namespace Fomoxa.Networking.Transports
{
    internal sealed class EncryptedUdpListener : IListenerTransport
    {
        internal const int PeerCeiling = 1024;

        private readonly Socket socket;
        private readonly ConnectTokens tokens;
        private readonly string target;
        private readonly Dictionary<ulong, EncryptedUdpPeer> sessions = new Dictionary<ulong, EncryptedUdpPeer>();
        private readonly DatagramCipher cipher = new DatagramCipher();
        private readonly byte[] datagram = new byte[EncryptedDatagram.MaxDatagram];
        private readonly byte[] outgoing = new byte[EncryptedDatagram.MaxDatagram];
        private readonly byte[] plain = new byte[EncryptedDatagram.MaxDatagram];
        private readonly byte[] opened = new byte[EncryptedDatagram.MaxDatagram];
        private bool released;

        public EncryptedUdpListener(IPEndPoint local, ConnectTokens tokens, string target)
        {
            this.tokens = tokens;
            this.target = target;
            socket = UdpSockets.Create(local.AddressFamily);
            socket.Bind(local);
        }

        public IPEndPoint LocalEndPoint => (IPEndPoint)socket.LocalEndPoint;

        public AcceptOutcome Accept()
        {
            if (released)
            {
                return AcceptOutcome.Error;
            }

            switch (UdpSockets.ReceiveFrom(socket, datagram, out int count, out EndPoint from))
            {
                case UdpReceiveStatus.WouldBlock:
                    return AcceptOutcome.Pending;
                case UdpReceiveStatus.Discard:
                    return AcceptOutcome.Progress;
                case UdpReceiveStatus.Closed:
                case UdpReceiveStatus.Error:
                    return AcceptOutcome.Error;
            }

            if (count < EncryptedDatagram.DataOverhead)
            {
                return AcceptOutcome.Progress;
            }

            var address = (IPEndPoint)from;
            ulong sessionId = EncryptedDatagram.ReadSessionId(datagram);
            sessions.TryGetValue(sessionId, out EncryptedUdpPeer known);
            byte kind = datagram[EncryptedDatagram.KindOffset];
            if (kind == EncryptedDatagram.DataKind)
            {
                known?.Open(EncryptedDatagram.DataHeaderSize, count, address);
                return AcceptOutcome.Progress;
            }

            if (kind != EncryptedDatagram.ConnectKind
                || !EncryptedDatagram.TryReadConnect(datagram, count, out int tokenOffset, out int tokenLength, out int attemptOffset, out int headerSize))
            {
                return AcceptOutcome.Progress;
            }

            byte[] token = new byte[tokenLength];
            Buffer.BlockCopy(datagram, tokenOffset, token, 0, tokenLength);
            byte[] attempt = new byte[EncryptedDatagram.AttemptSize];
            Buffer.BlockCopy(datagram, attemptOffset, attempt, 0, attempt.Length);
            if (known != null && known.IsAttempt(token, attempt))
            {
                known.Open(headerSize, count, address);
                return AcceptOutcome.Progress;
            }

            return Connect(sessionId, known, token, attempt, headerSize, count, address);
        }

        public void Dispose()
        {
            if (released)
            {
                return;
            }

            released = true;
            sessions.Clear();
            socket.Dispose();
        }

        internal SendOutcome SendTo(EncryptedUdpPeer peer, ReadOnlySpan<byte> bytes)
        {
            if (released)
            {
                return SendOutcome.Closed;
            }

            if (EncryptedDatagram.DataOverhead + bytes.Length > EncryptedDatagram.MaxDatagram)
            {
                return SendOutcome.TooLarge;
            }

            ulong sequence = peer.NextSequence();
            EncryptedDatagram.WriteDataHeader(outgoing, peer.SessionId, EncryptedDatagram.DataKind, sequence);
            bytes.CopyTo(plain);
            int sealedLength = cipher.Seal(peer.SendKey, sequence, outgoing, EncryptedDatagram.DataHeaderSize, plain, 0, bytes.Length, outgoing, EncryptedDatagram.DataHeaderSize);
            return UdpSockets.SendTo(socket, outgoing, EncryptedDatagram.DataHeaderSize + sealedLength, peer.Address);
        }

        internal bool TryOpen(EncryptedUdpPeer peer, int headerSize, int count, out ReadOnlyMemory<byte> frame)
        {
            frame = default;
            ulong sequence = EncryptedDatagram.ReadSequence(datagram);
            if (!peer.IsFresh(sequence)
                || !cipher.TryOpen(peer.ReceiveKey, sequence, datagram, headerSize, datagram, headerSize, count - headerSize, opened, 0, out int length))
            {
                return false;
            }

            peer.Mark(sequence);
            frame = new ReadOnlyMemory<byte>(opened, 0, length);
            return true;
        }

        internal void Forget(EncryptedUdpPeer peer)
        {
            if (sessions.TryGetValue(peer.SessionId, out EncryptedUdpPeer current) && current == peer)
            {
                sessions.Remove(peer.SessionId);
            }
        }

        private AcceptOutcome Connect(ulong sessionId, EncryptedUdpPeer known, byte[] token, byte[] attempt, int headerSize, int count, IPEndPoint address)
        {
            if (!tokens.TryOpen(token, target, out OpenedConnectToken openedToken)
                || openedToken.SessionId != sessionId
                || (known != null && !known.HasTokenNonce(openedToken.Nonce))
                || (known == null && sessions.Count >= PeerCeiling)
                || !tokens.TryUseAttempt(openedToken, attempt))
            {
                return AcceptOutcome.Progress;
            }

            var created = new EncryptedUdpPeer(
                this,
                sessionId,
                address,
                token,
                attempt,
                openedToken.Nonce,
                new KeyParameter(DatagramCipher.DeriveClientKey(openedToken.ClientKey, attempt)),
                new KeyParameter(DatagramCipher.DeriveServerKey(openedToken.ServerKey, attempt)),
                openedToken.Data);
            if (!TryOpen(created, headerSize, count, out ReadOnlyMemory<byte> frame))
            {
                return AcceptOutcome.Progress;
            }

            known?.Replace();
            created.Enqueue(frame);
            sessions[sessionId] = created;
            return AcceptOutcome.Accepted(created);
        }
    }

    internal sealed class EncryptedUdpPeer : ITransport, IConnectionData
    {
        internal const int QueueCeiling = 64;

        private readonly EncryptedUdpListener owner;
        private readonly byte[] token;
        private readonly byte[] attempt;
        private readonly byte[] tokenNonce;
        private readonly byte[] data;
        private readonly Queue<byte[]> inbox = new Queue<byte[]>();
        private ReplayWindow window;
        private ulong nextSequence;
        private bool released;

        public EncryptedUdpPeer(EncryptedUdpListener owner, ulong sessionId, IPEndPoint address, byte[] token, byte[] attempt, byte[] tokenNonce, KeyParameter receiveKey, KeyParameter sendKey, byte[] data)
        {
            this.owner = owner;
            SessionId = sessionId;
            Address = address;
            this.token = token;
            this.attempt = attempt;
            this.tokenNonce = tokenNonce;
            ReceiveKey = receiveKey;
            SendKey = sendKey;
            this.data = data;
        }

        public TransportKind Kind => TransportKind.Message;

        public ReadOnlyMemory<byte> ConnectionData => data;

        internal ulong SessionId { get; }

        internal IPEndPoint Address { get; private set; }

        internal KeyParameter ReceiveKey { get; }

        internal KeyParameter SendKey { get; }

        public SendOutcome Send(ReadOnlySpan<byte> bytes) =>
            released ? SendOutcome.Closed : owner.SendTo(this, bytes);

        public ReceiveOutcome Receive(Span<byte> buffer)
        {
            if (inbox.Count == 0)
            {
                return released ? ReceiveOutcome.Closed : ReceiveOutcome.WouldBlock;
            }

            byte[] packet = inbox.Peek();
            if (packet.Length > buffer.Length)
            {
                return ReceiveOutcome.NeedCapacity(packet.Length);
            }

            packet.CopyTo(buffer);
            inbox.Dequeue();
            return ReceiveOutcome.Ok(packet.Length);
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
            inbox.Clear();
            owner.Forget(this);
        }

        internal bool IsAttempt(byte[] otherToken, byte[] otherAttempt) =>
            SameBytes(token, otherToken) && SameBytes(attempt, otherAttempt);

        internal bool HasTokenNonce(byte[] nonce) => SameBytes(tokenNonce, nonce);

        internal bool IsFresh(ulong sequence) => window.IsFresh(sequence);

        internal void Mark(ulong sequence) => window.Mark(sequence);

        internal ulong NextSequence() => nextSequence++;

        internal void Open(int headerSize, int count, IPEndPoint from)
        {
            if (released || !owner.TryOpen(this, headerSize, count, out ReadOnlyMemory<byte> frame))
            {
                return;
            }

            Address = from;
            Enqueue(frame);
        }

        internal void Enqueue(ReadOnlyMemory<byte> frame)
        {
            if (inbox.Count >= QueueCeiling)
            {
                inbox.Dequeue();
            }

            inbox.Enqueue(frame.ToArray());
        }

        internal void Replace()
        {
            released = true;
            inbox.Clear();
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
