using System;

namespace Fomoxa.Networking
{
    public enum Channel
    {
        Unreliable,
        ReliableOrdered,
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class NetworkChannelAttribute : Attribute
    {
        public NetworkChannelAttribute(string codec, Channel channel)
        {
            Codec = codec;
            Channel = channel;
        }

        public string Codec { get; }

        public Channel Channel { get; }
    }
}
