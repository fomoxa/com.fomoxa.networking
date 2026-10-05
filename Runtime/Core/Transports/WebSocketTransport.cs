using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using Fomoxa.Net;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    internal sealed class WebSocketTransport : ITransport
    {
        public const int MaxMessageBytes = FomoxaWire.MaxDataFrameSize;

        private const byte Final = 0x80;
        private const byte Continuation = 0x0;
        private const byte Text = 0x1;
        private const byte Binary = 0x2;
        private const byte Close = 0x8;
        private const byte Ping = 0x9;
        private const byte Pong = 0xA;
        private const int MaxControlPayload = 125;
        private const int MaxPendingControlBytes = 64 * 1024;
        private const ushort NormalClosure = 1000;

        private readonly Socket socket;
        private readonly bool masksOutgoing;
        private readonly RandomNumberGenerator random;
        private readonly byte[] maskKey = new byte[4];
        private byte[] inbound = new byte[4096];
        private int inboundStart;
        private int inboundEnd;
        private byte[] outbound = new byte[4096];
        private int outboundStart;
        private int outboundEnd;
        private byte[] assembly = Array.Empty<byte>();
        private int assembled;
        private bool assembling;
        private bool messageReady;
        private bool closeSent;
        private bool closeReceived;
        private bool errored;
        private bool released;

        public WebSocketTransport(Socket socket, bool masksOutgoing, ReadOnlySpan<byte> alreadyReceived)
        {
            this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
            this.masksOutgoing = masksOutgoing;
            socket.Blocking = false;
            socket.NoDelay = true;
            if (masksOutgoing)
            {
                random = RandomNumberGenerator.Create();
            }

            EnsureInbound(alreadyReceived.Length);
            alreadyReceived.CopyTo(inbound);
            inboundEnd = alreadyReceived.Length;
        }

        public TransportKind Kind => TransportKind.Message;

        public SendOutcome Send(ReadOnlySpan<byte> bytes)
        {
            if (released || closeSent)
            {
                return SendOutcome.Closed;
            }

            if (errored)
            {
                return SendOutcome.Error;
            }

            if (bytes.Length > MaxMessageBytes)
            {
                return SendOutcome.TooLarge;
            }

            if (!Flush())
            {
                return errored ? SendOutcome.Error : SendOutcome.WouldBlock;
            }

            AppendFrame(Binary, bytes);
            Flush();
            return errored ? SendOutcome.Error : SendOutcome.Ok;
        }

        public ReceiveOutcome Receive(Span<byte> buffer)
        {
            if (released || closeReceived)
            {
                return ReceiveOutcome.Closed;
            }

            if (errored)
            {
                return ReceiveOutcome.Error;
            }

            Flush();
            while (!errored)
            {
                if (messageReady)
                {
                    return Deliver(buffer);
                }

                FrameStatus status = TakeFrame(buffer, out ReceiveOutcome delivered);
                if (status == FrameStatus.Delivered)
                {
                    return delivered;
                }

                if (status == FrameStatus.Closed)
                {
                    return ReceiveOutcome.Closed;
                }

                if (status == FrameStatus.NeedBytes && !ReadSocket())
                {
                    return errored ? ReceiveOutcome.Error : ReceiveOutcome.WouldBlock;
                }
            }

            return ReceiveOutcome.Error;
        }

        public void CloseGracefully()
        {
            if (released || errored || closeSent)
            {
                return;
            }

            SendClose(NormalClosure);
        }

        public void Dispose()
        {
            if (released)
            {
                return;
            }

            released = true;
            random?.Dispose();
            socket.Dispose();
        }

        private FrameStatus TakeFrame(Span<byte> buffer, out ReceiveOutcome delivered)
        {
            delivered = default;
            int available = inboundEnd - inboundStart;
            if (available < 2)
            {
                return FrameStatus.NeedBytes;
            }

            byte first = inbound[inboundStart];
            byte second = inbound[inboundStart + 1];
            bool final = (first & Final) != 0;
            byte opcode = (byte)(first & 0x0F);
            bool masked = (second & 0x80) != 0;
            int lengthCode = second & 0x7F;
            int headerLength = 2 + (lengthCode == 126 ? 2 : lengthCode == 127 ? 8 : 0) + (masked ? 4 : 0);
            if ((first & 0x70) != 0 || masked == masksOutgoing)
            {
                return Fail();
            }

            if (available < headerLength)
            {
                return FrameStatus.NeedBytes;
            }

            ulong length = lengthCode == 126
                ? BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(inbound, inboundStart + 2, 2))
                : lengthCode == 127
                    ? BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(inbound, inboundStart + 2, 8))
                    : (ulong)lengthCode;
            bool control = (opcode & 0x8) != 0;
            if (control && (!final || length > MaxControlPayload))
            {
                return Fail();
            }

            if (length > (ulong)(MaxMessageBytes - (assembling ? assembled : 0)))
            {
                return Fail();
            }

            int payloadLength = (int)length;
            if (available < headerLength + payloadLength)
            {
                EnsureInbound(headerLength + payloadLength);
                return FrameStatus.NeedBytes;
            }

            int maskAt = inboundStart + headerLength - (masked ? 4 : 0);
            int payloadAt = inboundStart + headerLength;
            var payload = new Span<byte>(inbound, payloadAt, payloadLength);
            if (masked)
            {
                for (int index = 0; index < payloadLength; index++)
                {
                    payload[index] ^= inbound[maskAt + (index & 3)];
                }
            }

            switch (opcode)
            {
                case Binary when !assembling:
                    if (final)
                    {
                        if (payloadLength > buffer.Length)
                        {
                            Keep(payload);
                            Consume(headerLength + payloadLength);
                            delivered = ReceiveOutcome.NeedCapacity(payloadLength);
                            return FrameStatus.Delivered;
                        }

                        payload.CopyTo(buffer);
                        Consume(headerLength + payloadLength);
                        delivered = ReceiveOutcome.Ok(payloadLength);
                        return FrameStatus.Delivered;
                    }

                    assembling = true;
                    assembled = 0;
                    Append(payload);
                    break;
                case Continuation when assembling:
                    Append(payload);
                    if (final)
                    {
                        assembling = false;
                        messageReady = true;
                    }

                    break;
                case Ping:
                    if (!closeSent && outboundEnd - outboundStart <= MaxPendingControlBytes)
                    {
                        AppendFrame(Pong, payload);
                        Flush();
                    }

                    break;
                case Pong:
                    break;
                case Close:
                    Consume(headerLength + payloadLength);
                    closeReceived = true;
                    if (!closeSent)
                    {
                        SendClose(payloadLength >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(payload) : NormalClosure);
                    }

                    return FrameStatus.Closed;
                default:
                    return Fail();
            }

            Consume(headerLength + payloadLength);
            return FrameStatus.Consumed;
        }

        private ReceiveOutcome Deliver(Span<byte> buffer)
        {
            if (assembled > buffer.Length)
            {
                return ReceiveOutcome.NeedCapacity(assembled);
            }

            new ReadOnlySpan<byte>(assembly, 0, assembled).CopyTo(buffer);
            messageReady = false;
            int length = assembled;
            assembled = 0;
            return ReceiveOutcome.Ok(length);
        }

        private void Keep(ReadOnlySpan<byte> payload)
        {
            assembled = 0;
            Append(payload);
            messageReady = true;
        }

        private void Append(ReadOnlySpan<byte> payload)
        {
            if (assembly.Length < assembled + payload.Length)
            {
                Array.Resize(ref assembly, Math.Max(assembled + payload.Length, assembly.Length * 2));
            }

            payload.CopyTo(new Span<byte>(assembly, assembled, payload.Length));
            assembled += payload.Length;
        }

        private void Consume(int count)
        {
            inboundStart += count;
            if (inboundStart == inboundEnd)
            {
                inboundStart = 0;
                inboundEnd = 0;
            }
        }

        private void EnsureInbound(int frameLength)
        {
            if (inbound.Length - inboundStart >= frameLength)
            {
                return;
            }

            int pending = inboundEnd - inboundStart;
            byte[] target = inbound.Length >= frameLength ? inbound : new byte[Math.Max(frameLength, inbound.Length * 2)];
            Buffer.BlockCopy(inbound, inboundStart, target, 0, pending);
            inbound = target;
            inboundStart = 0;
            inboundEnd = pending;
        }

        private bool ReadSocket()
        {
            if (inboundEnd == inbound.Length)
            {
                EnsureInbound(inbound.Length - inboundStart + 1);
            }

            int read = socket.Receive(new Span<byte>(inbound, inboundEnd, inbound.Length - inboundEnd), SocketFlags.None, out SocketError error);
            if (error == SocketError.WouldBlock)
            {
                return false;
            }

            if (error != SocketError.Success || read == 0)
            {
                errored = true;
                return false;
            }

            inboundEnd += read;
            return true;
        }

        private void SendClose(ushort code)
        {
            Span<byte> payload = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(payload, code);
            AppendFrame(Close, payload);
            closeSent = true;
            Flush();
        }

        private void AppendFrame(byte opcode, ReadOnlySpan<byte> payload)
        {
            int lengthBytes = payload.Length <= MaxControlPayload ? 0 : payload.Length <= ushort.MaxValue ? 2 : 8;
            int headerLength = 2 + lengthBytes + (masksOutgoing ? 4 : 0);
            EnsureOutbound(headerLength + payload.Length);
            int at = outboundEnd;
            outbound[at] = (byte)(Final | opcode);
            byte maskBit = masksOutgoing ? (byte)0x80 : (byte)0;
            if (lengthBytes == 0)
            {
                outbound[at + 1] = (byte)(maskBit | payload.Length);
            }
            else if (lengthBytes == 2)
            {
                outbound[at + 1] = (byte)(maskBit | 126);
                BinaryPrimitives.WriteUInt16BigEndian(new Span<byte>(outbound, at + 2, 2), (ushort)payload.Length);
            }
            else
            {
                outbound[at + 1] = (byte)(maskBit | 127);
                BinaryPrimitives.WriteUInt64BigEndian(new Span<byte>(outbound, at + 2, 8), (ulong)payload.Length);
            }

            int payloadAt = at + headerLength;
            payload.CopyTo(new Span<byte>(outbound, payloadAt, payload.Length));
            if (masksOutgoing)
            {
                random.GetBytes(maskKey);
                Buffer.BlockCopy(maskKey, 0, outbound, payloadAt - 4, 4);
                for (int index = 0; index < payload.Length; index++)
                {
                    outbound[payloadAt + index] ^= maskKey[index & 3];
                }
            }

            outboundEnd = payloadAt + payload.Length;
        }

        private void EnsureOutbound(int frameLength)
        {
            if (outbound.Length - outboundEnd >= frameLength)
            {
                return;
            }

            int pending = outboundEnd - outboundStart;
            byte[] target = outbound.Length >= pending + frameLength ? outbound : new byte[Math.Max(pending + frameLength, outbound.Length * 2)];
            Buffer.BlockCopy(outbound, outboundStart, target, 0, pending);
            outbound = target;
            outboundStart = 0;
            outboundEnd = pending;
        }

        private bool Flush()
        {
            while (outboundStart < outboundEnd)
            {
                int sent = socket.Send(new ReadOnlySpan<byte>(outbound, outboundStart, outboundEnd - outboundStart), SocketFlags.None, out SocketError error);
                if (error != SocketError.Success && error != SocketError.WouldBlock)
                {
                    errored = true;
                    return false;
                }

                if (sent <= 0)
                {
                    return false;
                }

                outboundStart += sent;
            }

            outboundStart = 0;
            outboundEnd = 0;
            return true;
        }

        private FrameStatus Fail()
        {
            errored = true;
            return FrameStatus.Failed;
        }

        private enum FrameStatus
        {
            NeedBytes,
            Consumed,
            Delivered,
            Closed,
            Failed,
        }
    }
}
