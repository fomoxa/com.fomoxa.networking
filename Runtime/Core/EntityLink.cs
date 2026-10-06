using System;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking
{
    internal sealed class EntityLink : IBehaviourLink
    {
        private readonly INetworkEntity entity;

        public EntityLink(INetworkEntity entity)
        {
            this.entity = entity;
        }

        public bool SpawnedOnServer => Server != null;

        public bool SpawnedOnClient => Client != null;

        public bool IsReplaying => Client != null && Client.IsReplaying;

        private ServerManager Server => entity.Record?.Server?.Owner as ServerManager;

        private ClientManager Client => entity.Record?.Client?.Owner as ClientManager;

        public RpcMessageIds RpcIds(bool server) => server ? Server.RpcIds : Client.RpcIds;

        public SendResult SendToServer(uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            Client.SendToObject(messageId, entity.Record.ObjectId, behaviourIndex, body);

        public int SendToObservers(uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            Server.BroadcastToObject(messageId, entity.Record.ObjectId, behaviourIndex, body);

        public SendResult SendToObserver(ulong peerId, uint messageId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            Server.SendToObserver(peerId, messageId, entity.Record.ObjectId, behaviourIndex, body);
    }
}
