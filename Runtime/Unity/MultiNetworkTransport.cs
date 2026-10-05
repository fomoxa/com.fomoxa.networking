using System;
using System.Collections.Generic;
using Fomoxa.Networking.Transports;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class MultiNetworkTransport : NetworkTransport
    {
        [SerializeField] private List<Entry> transports = new List<Entry>();

        public override ITransportFactory CreateFactory()
        {
            var factories = new List<ITransportFactory>();
            var ports = new List<ushort>();
            foreach (Entry entry in transports)
            {
                if (entry.Transport == null)
                {
                    throw new InvalidOperationException($"{name} has an empty transport entry");
                }

                ITransportFactory factory;
                try
                {
                    factory = entry.Transport.CreateFactory();
                }
                catch (PlatformNotSupportedException) when (RunsInBrowser)
                {
                    continue;
                }

                factories.Add(factory ?? throw new InvalidOperationException($"{entry.Transport.GetType().Name} returned no transport factory"));
                ports.Add(entry.Port >= 0 && entry.Port <= ushort.MaxValue
                    ? (ushort)entry.Port
                    : throw new InvalidOperationException($"{name} has port {entry.Port} for {entry.Transport.GetType().Name}; a port is 0 to 65535"));
            }

            if (factories.Count == 0)
            {
                throw RunsInBrowser
                    ? NotInBrowser(name)
                    : new InvalidOperationException($"{name} has no transports");
            }

            return new MultiTransportFactory(factories, ports);
        }

        internal void Add(NetworkTransport transport, int port) =>
            transports.Add(new Entry { Transport = transport, Port = port });

        [Serializable]
        private sealed class Entry
        {
            [SerializeField] private NetworkTransport transport;
            [SerializeField] private int port;

            public NetworkTransport Transport
            {
                get => transport;
                set => transport = value;
            }

            public int Port
            {
                get => port;
                set => port = value;
            }
        }
    }
}
