using System;
using BundleFixture;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking.Tests
{
    internal static class SessionAcceptance
    {
        public static ServerSession SendingLocalPeer(this ServerSession server)
        {
            server.SetPeerStartHandler((ulong peerId, out uint failedMessageId, out Exception exception) =>
            {
                failedMessageId = 0;
                exception = null;
                server.EnqueueObjectMessage(peerId, TestBundles.LocalPeerId, ReadOnlySpan<byte>.Empty);
                return true;
            });
            return server;
        }

        public static ClientSession AcceptingLocalPeer(this ClientSession client)
        {
            client.SetAcceptance(TestBundles.LocalPeerId);
            return client;
        }
    }
}
