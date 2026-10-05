using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class NetworkRpcAttribute : Attribute
    {
        public NetworkRpcAttribute()
        {
        }

        public NetworkRpcAttribute(string model)
        {
            Model = model;
        }

        public string Model { get; }

        public Channel Channel { get; set; } = Channel.ReliableOrdered;
    }
}
