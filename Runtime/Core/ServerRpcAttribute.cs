using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ServerRpcAttribute : Attribute
    {
        public ServerRpcAttribute()
        {
        }

        public ServerRpcAttribute(string model)
        {
            Model = model;
        }

        public string Model { get; }

        public string Codec { get; set; }

        public Channel Channel { get; set; } = Channel.ReliableOrdered;

        public bool RequireOwnership { get; set; } = true;
    }
}
