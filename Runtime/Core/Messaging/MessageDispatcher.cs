using System;
using System.Collections.Generic;
using Fomoxa.Net;

namespace Fomoxa.Networking.Messaging
{
    public delegate void PayloadHandler(ulong peerId, ReadOnlyMemory<byte> payload);

    public delegate void ObjectPayloadHandler(ulong peerId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body);

    public sealed class HandlerRegistrationException : Exception
    {
        public HandlerRegistrationException(string message) : base(message)
        {
        }
    }

    public sealed class MessageDispatcher
    {
        private readonly Schema schema;
        private readonly Dictionary<uint, PayloadHandler> handlers = new Dictionary<uint, PayloadHandler>();
        private readonly Dictionary<uint, ObjectPayloadHandler> objectHandlers = new Dictionary<uint, ObjectPayloadHandler>();

        public MessageDispatcher(Schema schema)
        {
            this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
        }

        public int HandlerCount => handlers.Count + objectHandlers.Count;

        public void Register(uint messageId, PayloadHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            EnsureRegistrable(messageId);
            handlers.Add(messageId, handler);
        }

        public void RegisterObject(uint messageId, ObjectPayloadHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            EnsureRegistrable(messageId);
            objectHandlers.Add(messageId, handler);
        }

        public bool IsObjectMessage(uint messageId) => objectHandlers.ContainsKey(messageId);

        public bool Dispatch(ulong peerId, uint messageId, ReadOnlyMemory<byte> data)
        {
            if (objectHandlers.TryGetValue(messageId, out ObjectPayloadHandler objectHandler))
            {
                ReadOnlySpan<byte> header = data.Span;
                objectHandler(
                    peerId,
                    ObjectHeader.ReadObjectId(header),
                    ObjectHeader.ReadBehaviourIndex(header),
                    data.Slice(ObjectHeader.UnreliableLength));
                return true;
            }

            if (!handlers.TryGetValue(messageId, out PayloadHandler handler))
            {
                return false;
            }

            handler(peerId, data);
            return true;
        }

        private void EnsureRegistrable(uint messageId)
        {
            if (schema.Message(messageId) == null)
            {
                throw new HandlerRegistrationException($"message id 0x{messageId:X8} is not declared in the schema");
            }

            if (handlers.ContainsKey(messageId) || objectHandlers.ContainsKey(messageId))
            {
                throw new HandlerRegistrationException($"message id 0x{messageId:X8} already has a handler");
            }
        }
    }
}
