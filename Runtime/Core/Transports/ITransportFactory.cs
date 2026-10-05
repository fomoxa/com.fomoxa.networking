using System;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    public enum ConnectStatus
    {
        Pending,
        Connected,
        Failed,
    }

    public interface ITransportFactory
    {
        int FrameBudget { get; }

        IListenerTransport CreateListener(ushort port, out ushort boundPort);

        ITransportConnector CreateConnector(string address, ushort port);
    }

    public interface ITransportConnector : IDisposable
    {
        ConnectStatus Poll(out ITransport transport);
    }
}
