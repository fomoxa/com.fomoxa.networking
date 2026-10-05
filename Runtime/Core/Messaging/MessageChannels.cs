using System.Collections.Generic;

namespace Fomoxa.Networking.Messaging
{
    public sealed class MessageChannels
    {
        private readonly Dictionary<uint, Channel> reliable = new Dictionary<uint, Channel>();

        public int ReliableCount => reliable.Count;

        public void Set(uint messageId, Channel channel)
        {
            if (channel == Channel.Unreliable)
            {
                reliable.Remove(messageId);
                return;
            }

            reliable[messageId] = channel;
        }

        public Channel Of(uint messageId) =>
            reliable.TryGetValue(messageId, out Channel channel) ? channel : Channel.Unreliable;

        public bool IsReliable(uint messageId) => reliable.ContainsKey(messageId);
    }
}
