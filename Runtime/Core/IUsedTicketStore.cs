using System;

namespace Fomoxa.Networking
{
    public interface IUsedTicketStore
    {
        bool TryUse(ReadOnlySpan<byte> nonce, DateTime expiresUtc, DateTime nowUtc);
    }
}
