using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ClientRpcAttribute : Attribute
    {
        public ClientRpcAttribute()
        {
        }

        public ClientRpcAttribute(string model)
        {
            Model = model;
        }

        public string Model { get; }

        public string Codec { get; set; }

        public Channel Channel { get; set; } = Channel.ReliableOrdered;
    }
}
