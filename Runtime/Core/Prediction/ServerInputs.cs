using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;

namespace Fomoxa.Networking.Prediction
{
    public readonly struct InputRejectedArgs
    {
        public InputRejectedArgs(ulong peerId, uint objectId, byte behaviourIndex)
        {
            PeerId = peerId;
            ObjectId = objectId;
            BehaviourIndex = behaviourIndex;
        }

        public ulong PeerId { get; }

        public uint ObjectId { get; }

        public byte BehaviourIndex { get; }
    }

    public sealed class ServerInputs
    {
        private readonly ServerObjects objects;
        private readonly InputProtocol protocol;
        private readonly ServerClock clock;
        private readonly Dictionary<uint, List<InputBuffer>> buffers = new Dictionary<uint, List<InputBuffer>>();
        private readonly Dictionary<ulong, PeerInputs> peers = new Dictionary<ulong, PeerInputs>();
        private InputFrames frames = new InputFrames();
        private int maxInputLead = 30;

        public ServerInputs(ServerSession session, ServerObjects objects, InputProtocol protocol, ServerClock clock)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            this.objects = objects ?? throw new ArgumentNullException(nameof(objects));
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            session.Dispatcher.RegisterObject(protocol.FramesCodec.MessageId, OnFrames);
            session.OnRemoteConnectionState += OnPeerState;
            session.OnServerConnectionState += OnServerState;
            objects.OnOwnerChanged += args => Forget(args.ObjectId);
            objects.OnDespawned += Forget;
            clock.InputLeadOf = InputLeadOf;
        }

        public event Action<InputRejectedArgs> OnInputRejected;

        public int MaxInputLead
        {
            get => maxInputLead;
            set => maxInputLead = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "the input lead limit is at least one tick");
        }

        public int DroppedOf(ulong peerId) => peers.TryGetValue(peerId, out PeerInputs peer) ? peer.Dropped : 0;

        public bool TakeInput(uint objectId, byte behaviourIndex, uint tick, out ReadOnlyMemory<byte> input, out bool repeated, out uint repeatedTicks)
        {
            InputBuffer buffer = Find(objectId, behaviourIndex, false);
            if (buffer == null)
            {
                input = default;
                repeated = false;
                repeatedTicks = 0;
                return false;
            }

            repeated = !buffer.TakeTick(tick);
            buffer.DropThrough(tick);
            input = buffer.Last;
            repeatedTicks = repeated && buffer.HasLast ? tick - buffer.LastTick : 0;
            return buffer.HasLast;
        }

        public void CountDropped(ulong peerId) => Peer(peerId).Dropped++;

        private void OnFrames(ulong peerId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!objects.TryGet(objectId, out ObjectRow row))
            {
                return;
            }

            if (row.OwnerId != peerId)
            {
                OnInputRejected?.Invoke(new InputRejectedArgs(peerId, objectId, behaviourIndex));
                return;
            }

            protocol.FramesCodec.Decode(body, ref frames);
            uint now = clock.Tick;
            PeerInputs peer = Peer(peerId);
            peer.Lead = (short)Math.Max(short.MinValue + 1, Math.Min(short.MaxValue, (long)frames.Tick - now));
            peer.ReceivedAt = now;
            peer.HasLead = true;
            if (frames.Tick < now || frames.Tick - now > (uint)maxInputLead)
            {
                peer.Dropped++;
                return;
            }

            InputBuffer buffer = Find(objectId, behaviourIndex, true);
            buffer.DropBefore(now);
            for (int index = 0; index < frames.Frames.Count && (uint)index <= frames.Tick - now; index++)
            {
                buffer.Store(frames.Tick - (uint)index, frames.Frames[index].Span);
            }
        }

        private short InputLeadOf(ulong peerId)
        {
            if (!peers.TryGetValue(peerId, out PeerInputs peer) || !peer.HasLead || clock.Tick - peer.ReceivedAt > (uint)maxInputLead)
            {
                return ClockProtocol.NoInputLead;
            }

            return peer.Lead;
        }

        private PeerInputs Peer(ulong peerId)
        {
            if (!peers.TryGetValue(peerId, out PeerInputs peer))
            {
                peer = new PeerInputs();
                peers.Add(peerId, peer);
            }

            return peer;
        }

        private InputBuffer Find(uint objectId, byte behaviourIndex, bool create)
        {
            if (!buffers.TryGetValue(objectId, out List<InputBuffer> list))
            {
                if (!create)
                {
                    return null;
                }

                list = new List<InputBuffer>();
                buffers.Add(objectId, list);
            }

            foreach (InputBuffer existing in list)
            {
                if (existing.BehaviourIndex == behaviourIndex)
                {
                    return existing;
                }
            }

            if (!create)
            {
                return null;
            }

            var buffer = new InputBuffer(behaviourIndex);
            list.Add(buffer);
            return buffer;
        }

        private void Forget(uint objectId) => buffers.Remove(objectId);

        private void OnPeerState(ConnectionStateArgs args)
        {
            if (args.State == ConnectionState.Stopped)
            {
                peers.Remove(args.PeerId);
            }
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.State == ServerState.Stopped)
            {
                buffers.Clear();
                peers.Clear();
            }
        }

        private sealed class PeerInputs
        {
            public short Lead { get; set; }

            public uint ReceivedAt { get; set; }

            public bool HasLead { get; set; }

            public int Dropped { get; set; }
        }

        private sealed class InputBuffer
        {
            private readonly List<Entry> entries = new List<Entry>();
            private readonly Stack<byte[]> spare = new Stack<byte[]>();
            private byte[] last = Array.Empty<byte>();
            private int lastLength;

            public InputBuffer(byte behaviourIndex)
            {
                BehaviourIndex = behaviourIndex;
            }

            public byte BehaviourIndex { get; }

            public bool HasLast { get; private set; }

            public uint LastTick { get; private set; }

            public ReadOnlyMemory<byte> Last => new ReadOnlyMemory<byte>(last, 0, lastLength);

            public void Store(uint tick, ReadOnlySpan<byte> input)
            {
                for (int index = 0; index < entries.Count; index++)
                {
                    if (entries[index].Tick == tick)
                    {
                        entries[index] = Fill(entries[index].Data, tick, input);
                        return;
                    }
                }

                entries.Add(Fill(spare.Count > 0 ? spare.Pop() : Array.Empty<byte>(), tick, input));
            }

            public bool TakeTick(uint tick)
            {
                for (int index = 0; index < entries.Count; index++)
                {
                    if (entries[index].Tick != tick)
                    {
                        continue;
                    }

                    Entry taken = entries[index];
                    entries.RemoveAt(index);
                    spare.Push(last);
                    last = taken.Data;
                    lastLength = taken.Length;
                    LastTick = tick;
                    HasLast = true;
                    return true;
                }

                return false;
            }

            public void DropBefore(uint tick)
            {
                if (tick > 0)
                {
                    DropThrough(tick - 1);
                }
            }

            public void DropThrough(uint tick)
            {
                for (int index = entries.Count - 1; index >= 0; index--)
                {
                    if (entries[index].Tick <= tick)
                    {
                        spare.Push(entries[index].Data);
                        entries.RemoveAt(index);
                    }
                }
            }

            private static Entry Fill(byte[] data, uint tick, ReadOnlySpan<byte> input)
            {
                if (data.Length < input.Length)
                {
                    data = new byte[input.Length];
                }

                input.CopyTo(data);
                return new Entry(tick, data, input.Length);
            }

            private readonly struct Entry
            {
                public Entry(uint tick, byte[] data, int length)
                {
                    Tick = tick;
                    Data = data;
                    Length = length;
                }

                public uint Tick { get; }

                public byte[] Data { get; }

                public int Length { get; }
            }
        }
    }
}
