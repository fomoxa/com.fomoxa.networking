using System;

namespace Fomoxa.Networking.Messaging
{
    public sealed class MessageDecodeException : Exception
    {
        public MessageDecodeException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
