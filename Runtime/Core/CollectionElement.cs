using System;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking
{
    internal abstract class CollectionElement<T>
    {
        public abstract uint MessageId { get; }

        public abstract ReadOnlyMemory<byte> Encode(T value);

        public abstract T Decode(ReadOnlyMemory<byte> bytes);
    }

    internal sealed class ModelElement<T> : CollectionElement<T>
        where T : class, new()
    {
        private readonly IMessageCodec<T> codec;

        public ModelElement(IMessageCodec<T> codec)
        {
            this.codec = codec;
        }

        public override uint MessageId => codec.MessageId;

        public override ReadOnlyMemory<byte> Encode(T value) => codec.Encode(value);

        public override T Decode(ReadOnlyMemory<byte> bytes)
        {
            var value = new T();
            codec.Decode(bytes, ref value);
            return value;
        }
    }

    internal sealed class ValueElement<T, TModel> : CollectionElement<T>
        where TModel : class, new()
    {
        private readonly IMessageCodec<TModel> codec;
        private readonly Func<TModel, T> read;
        private readonly Action<TModel, T> write;
        private TModel scratch = new TModel();

        public ValueElement(IMessageCodec<TModel> codec, Func<TModel, T> read, Action<TModel, T> write)
        {
            this.codec = codec;
            this.read = read;
            this.write = write;
        }

        public override uint MessageId => codec.MessageId;

        public override ReadOnlyMemory<byte> Encode(T value)
        {
            write(scratch, value);
            return codec.Encode(scratch);
        }

        public override T Decode(ReadOnlyMemory<byte> bytes)
        {
            codec.Decode(bytes, ref scratch);
            return read(scratch);
        }
    }
}
