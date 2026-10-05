using System;
using System.Collections.Generic;

namespace Fomoxa.Networking
{
    public sealed class RpcMessageIds
    {
        private readonly Dictionary<(string Type, string Method), uint> declared = new Dictionary<(string Type, string Method), uint>();
        private readonly Dictionary<(Type Type, string Method), uint> resolved = new Dictionary<(Type Type, string Method), uint>();

        public int Count => declared.Count;

        public void Set(string type, string method, uint messageId)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            declared[(type, method)] = messageId;
            resolved.Clear();
        }

        public bool TryGet(Type type, string method, out uint messageId)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            if (resolved.TryGetValue((type, method), out messageId))
            {
                return true;
            }

            for (Type current = type; current != null; current = current.BaseType)
            {
                if (current.FullName != null && declared.TryGetValue((current.FullName, method), out messageId))
                {
                    resolved[(type, method)] = messageId;
                    return true;
                }
            }

            messageId = 0;
            return false;
        }
    }
}
