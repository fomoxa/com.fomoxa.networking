using System;
using Fomoxa.Net.Transports;
using Fomoxa.Networking;
using Fomoxa.Networking.Transports;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class EncryptedUdpNetworkTransport : NetworkTransport
    {
        [SerializeField] private string target = "";

        public string Target
        {
            get => target;
            set => target = value;
        }

        public ConnectTokens Tokens { get; set; }

        public FomoxaRegistry Registry { get; set; }

        public override ITransportFactory CreateFactory()
        {
            if (RunsInBrowser)
            {
                throw NotInBrowser(nameof(EncryptedUdpNetworkTransport));
            }

            FomoxaRegistry registry = Registry ?? FomoxaRegistry.Default;
            return new ComponentFactory(this, registry, new EncryptedUdpTransportFactory(registry));
        }

        private sealed class ComponentFactory : IConnectionTransportFactory
        {
            private readonly EncryptedUdpNetworkTransport owner;
            private readonly FomoxaRegistry registry;
            private readonly EncryptedUdpTransportFactory client;

            public ComponentFactory(EncryptedUdpNetworkTransport owner, FomoxaRegistry registry, EncryptedUdpTransportFactory client)
            {
                this.owner = owner;
                this.registry = registry;
                this.client = client;
            }

            public int FrameBudget => client.FrameBudget;

            public bool RequiresConnection => client.RequiresConnection;

            public bool CanUseConnection(ReadOnlyMemory<byte> connection) => client.CanUseConnection(connection);

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                EncryptedUdpTransportFactory server = owner.Tokens == null
                    ? client
                    : new EncryptedUdpTransportFactory(registry, owner.Tokens, owner.target ?? "");
                return server.CreateListener(port, out boundPort);
            }

            public ITransportConnector CreateConnector(string address, ushort port) => client.CreateConnector(address, port);

            public ITransportConnector CreateConnector(string address, ushort port, ReadOnlyMemory<byte> connection) =>
                client.CreateConnector(address, port, connection);
        }
    }
}
