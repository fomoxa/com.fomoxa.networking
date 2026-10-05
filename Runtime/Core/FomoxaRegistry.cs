using System;
using System.Collections.Generic;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking
{
    public sealed class FomoxaRegistry
    {
        private readonly Dictionary<Type, object> codecs = new Dictionary<Type, object>();

        public static FomoxaRegistry Default { get; } = new FomoxaRegistry();

        public Schema Schema { get; private set; }

        public MessageChannels Channels { get; } = new MessageChannels();

        public RpcMessageIds Rpcs { get; } = new RpcMessageIds();

        public void SetSchema(Schema schema)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        }

        public void SetCodec<T>(IMessageCodec<T> codec)
        {
            codecs[typeof(T)] = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public IMessageCodec<T> Codec<T>() =>
            codecs.TryGetValue(typeof(T), out object codec) ? (IMessageCodec<T>)codec : null;
    }
}
