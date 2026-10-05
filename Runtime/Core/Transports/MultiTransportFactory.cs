using System;
using System.Collections.Generic;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    public sealed class MultiTransportFactory : ITransportFactory
    {
        private readonly ITransportFactory[] factories;
        private readonly ushort[] ports;

        public MultiTransportFactory(IReadOnlyList<ITransportFactory> factories, IReadOnlyList<ushort> ports)
        {
            if (factories == null || factories.Count == 0)
            {
                throw new ArgumentException("a multi transport needs at least one transport", nameof(factories));
            }

            if (ports == null || ports.Count != factories.Count)
            {
                throw new ArgumentException("every transport needs a port entry", nameof(ports));
            }

            this.factories = new ITransportFactory[factories.Count];
            this.ports = new ushort[ports.Count];
            for (int index = 0; index < factories.Count; index++)
            {
                this.factories[index] = factories[index] ?? throw new ArgumentException("a transport factory is missing", nameof(factories));
                this.ports[index] = ports[index];
            }
        }

        public int FrameBudget => factories[0].FrameBudget;

        public int Count => factories.Length;

        public IListenerTransport CreateListener(ushort port, out ushort boundPort)
        {
            var listeners = new IListenerTransport[factories.Length];
            var budgets = new int[factories.Length];
            boundPort = 0;
            try
            {
                for (int index = 0; index < factories.Length; index++)
                {
                    listeners[index] = factories[index].CreateListener(index == 0 ? port : ports[index], out ushort bound);
                    budgets[index] = factories[index].FrameBudget;
                    if (index == 0)
                    {
                        boundPort = bound;
                    }
                }
            }
            catch
            {
                foreach (IListenerTransport created in listeners)
                {
                    created?.Dispose();
                }

                throw;
            }

            return new CompositeListener(listeners, budgets);
        }

        public ITransportConnector CreateConnector(string address, ushort port) =>
            factories[0].CreateConnector(address, port);
    }
}
