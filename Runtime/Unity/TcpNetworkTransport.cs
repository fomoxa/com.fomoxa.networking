using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class TcpNetworkTransport : NetworkTransport
    {
        [SerializeField] private bool useTls;

        public bool UseTls
        {
            get => useTls;
            set => useTls = value;
        }

        public TlsSettings Tls { get; } = new TlsSettings();

        public override ITransportFactory CreateFactory() =>
            RunsInBrowser ? throw NotInBrowser(nameof(TcpNetworkTransport)) : new ComponentFactory(this);

        private sealed class ComponentFactory : ITransportFactory
        {
            private readonly TcpNetworkTransport owner;
            private readonly TcpTransportFactory plain = new TcpTransportFactory();

            public ComponentFactory(TcpNetworkTransport owner)
            {
                this.owner = owner;
            }

            public int FrameBudget => plain.FrameBudget;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort) => plain.CreateListener(port, out boundPort);

            public ITransportConnector CreateConnector(string address, ushort port) =>
                owner.useTls ? new TcpTransportFactory(owner.Tls).CreateConnector(address, port) : plain.CreateConnector(address, port);
        }
    }
}
