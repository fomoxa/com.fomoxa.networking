using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class MessageAdapterAttribute : Attribute
    {
        public MessageAdapterAttribute(string codec)
        {
            Codec = codec;
        }

        public string Codec { get; }
    }
}
