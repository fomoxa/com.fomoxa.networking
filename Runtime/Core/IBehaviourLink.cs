using System;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking
{
    internal interface IBehaviourLink
    {
        bool SpawnedOnServer { get; }

        bool SpawnedOnClient { get; }

        bool IsReplaying { get; }

        RpcMessageIds RpcIds(bool server);

        SendResult SendToServer(uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body);

        int SendToObservers(uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body);

        SendResult SendToObserver(ulong peerId, uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body);
    }
}
