using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Objects
{
    public sealed class CollectionProtocol
    {
        private readonly Dictionary<Type, object> values = new Dictionary<Type, object>();

        private CollectionProtocol(
            IMessageCodec<CollectionChanges> changesCodec,
            IMessageCodec<CollectionContent> contentCodec,
            IMessageCodec<CollectionContents> contentsCodec,
            IMessageCodec<CollectionResync> resyncCodec,
            MessageChannels channels)
        {
            ChangesCodec = changesCodec;
            ContentCodec = contentCodec;
            ContentsCodec = contentsCodec;
            ResyncCodec = resyncCodec;
            ObjectProtocol.EnsureReliable(channels, changesCodec.MessageId, nameof(CollectionChanges));
            ObjectProtocol.EnsureReliable(channels, contentCodec.MessageId, nameof(CollectionContent));
            ObjectProtocol.EnsureReliable(channels, resyncCodec.MessageId, nameof(CollectionResync));
        }

        public IMessageCodec<CollectionChanges> ChangesCodec { get; }

        public IMessageCodec<CollectionContent> ContentCodec { get; }

        public IMessageCodec<CollectionContents> ContentsCodec { get; }

        public IMessageCodec<CollectionResync> ResyncCodec { get; }

        public static CollectionProtocol From(FomoxaRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var protocol = new CollectionProtocol(
                Require<CollectionChanges>(registry),
                Require<CollectionContent>(registry),
                Require<CollectionContents>(registry),
                Require<CollectionResync>(registry),
                registry.Channels);
            protocol.AddValue<bool, BoolValue>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<sbyte, I8Value>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<byte, U8Value>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<short, I16Value>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<ushort, U16Value>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<int, I32Value>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<uint, U32Value>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<long, I64Value>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<ulong, U64Value>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<float, F32Value>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<double, F64Value>(registry, model => model.Value, (model, value) => model.Value = value);
            protocol.AddValue<string, StringValue>(registry, model => model.Value, (model, value) => model.Value = value);
            return protocol;
        }

        internal bool TryGetValue<T>(out CollectionElement<T> element)
        {
            if (values.TryGetValue(typeof(T), out object found))
            {
                element = (CollectionElement<T>)found;
                return true;
            }

            element = null;
            return false;
        }

        private void AddValue<T, TModel>(FomoxaRegistry registry, Func<TModel, T> read, Action<TModel, T> write)
            where TModel : class, new()
        {
            values.Add(typeof(T), new ValueElement<T, TModel>(Require<TModel>(registry), read, write));
        }

        private static IMessageCodec<T> Require<T>(FomoxaRegistry registry) =>
            registry.Codec<T>()
            ?? throw new InvalidOperationException($"no {typeof(T).Name} codec is registered; FomoxaAdapters.RegisterAll has not run");
    }
}
