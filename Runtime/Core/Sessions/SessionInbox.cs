using System;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Sessions
{
    public sealed class SessionInbox
    {
        private readonly SessionProtocol protocol;
        private readonly MessageDispatcher dispatcher;
        private readonly BundleReader reader;
        private readonly Action<ulong, int> entryDropped;
        private ReliableAck ack = new ReliableAck();
        private PeerLeave leave = new PeerLeave();
        private bool leaving;
        private uint failedMessageId;
        private Exception failure;

        public SessionInbox(SessionProtocol protocol, MessageDispatcher dispatcher, Action<ulong, int> entryDropped)
        {
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.entryDropped = entryDropped ?? throw new ArgumentNullException(nameof(entryDropped));
            reader = new BundleReader(protocol.Bundle);
        }

        public int Count => reader.Count;

        public bool HasFailed => failure != null;

        public bool TryRead(uint messageId, ReadOnlyMemory<byte> payload, ReliableChannel reliable)
        {
            leaving = false;
            if (!reader.TryRead(messageId, payload))
            {
                return false;
            }

            for (int index = 0; index < reader.Count; index++)
            {
                if (!IsWellFormed(reader.MessageIdAt(index), reader.DataAt(index), reliable))
                {
                    reader.Release();
                    return false;
                }
            }

            return true;
        }

        public void Deliver(int index, ulong peerId, ReliableChannel reliable, TimeSpan now)
        {
            uint messageId = reader.MessageIdAt(index);
            ReadOnlyMemory<byte> data = reader.DataAt(index);
            if (messageId == protocol.AckCodec.MessageId)
            {
                protocol.AckCodec.Decode(data, ref ack);
                reliable.OnAck(ack.Next, ack.Received, now);
                return;
            }

            if (messageId == protocol.LeaveCodec.MessageId)
            {
                leaving = true;
                return;
            }

            if (!protocol.Channels.IsReliable(messageId))
            {
                Dispatch(peerId, messageId, data);
                return;
            }

            reliable.MarkReceived();
            ushort seq = SeqHeader.Read(data.Span);
            ReadOnlyMemory<byte> afterSeq = data.Slice(SeqHeader.Length);
            switch (reliable.Classify(seq))
            {
                case ReliableVerdict.Deliver:
                    reliable.Advance();
                    Dispatch(peerId, messageId, afterSeq);
                    break;
                case ReliableVerdict.Buffer:
                    reliable.Store(seq, messageId, afterSeq.Span);
                    break;
            }
        }

        public bool DeliverNextBuffered(ulong peerId, ReliableChannel reliable)
        {
            if (!reliable.TryTakeNext(out uint messageId, out byte[] data))
            {
                return false;
            }

            Dispatch(peerId, messageId, data);
            return true;
        }

        public bool TakeFailure(out uint messageId, out Exception exception)
        {
            messageId = failedMessageId;
            exception = failure;
            failure = null;
            return exception != null;
        }

        public bool TakeLeave()
        {
            bool taken = leaving;
            leaving = false;
            return taken;
        }

        public void Release()
        {
            reader.Release();
        }

        private void Dispatch(ulong peerId, uint messageId, ReadOnlyMemory<byte> data)
        {
            try
            {
                dispatcher.Dispatch(peerId, messageId, data);
            }
            catch (MessageDecodeException)
            {
                entryDropped(peerId, data.Length + (protocol.Channels.IsReliable(messageId) ? SeqHeader.Length : 0));
            }
            catch (Exception exception)
            {
                failedMessageId = messageId;
                failure = exception;
            }
        }

        private bool IsWellFormed(uint messageId, ReadOnlyMemory<byte> data, ReliableChannel reliable)
        {
            if (messageId == protocol.AckCodec.MessageId)
            {
                try
                {
                    protocol.AckCodec.Decode(data, ref ack);
                    return true;
                }
                catch (MessageDecodeException)
                {
                    return false;
                }
            }

            if (messageId == protocol.LeaveCodec.MessageId)
            {
                try
                {
                    protocol.LeaveCodec.Decode(data, ref leave);
                    return true;
                }
                catch (MessageDecodeException)
                {
                    return false;
                }
            }

            bool isReliable = protocol.Channels.IsReliable(messageId);
            int headerLength = (isReliable ? SeqHeader.Length : 0)
                + (dispatcher.IsObjectMessage(messageId) ? ObjectHeader.UnreliableLength : 0);
            if (data.Length < headerLength)
            {
                return false;
            }

            return !isReliable || reliable.Classify(SeqHeader.Read(data.Span)) != ReliableVerdict.OutOfWindow;
        }
    }
}
