using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Fomoxa.Networking.Transports
{
    public enum ResolveStatus
    {
        Pending,
        Resolved,
        Failed,
    }

    public interface IHostResolver
    {
        ResolveStatus Poll(out IPAddress[] resolved);
    }

    public sealed class HostResolver : IHostResolver
    {
        private readonly Task<IPAddress[]> resolving;
        private IPAddress[] addresses;

        public HostResolver(string host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (IPAddress.TryParse(host, out IPAddress literal))
            {
                addresses = new[] { literal };
            }
            else
            {
                resolving = Dns.GetHostAddressesAsync(host);
            }
        }

        public ResolveStatus Poll(out IPAddress[] resolved)
        {
            resolved = null;
            if (addresses == null)
            {
                if (!resolving.IsCompleted)
                {
                    return ResolveStatus.Pending;
                }

                if (resolving.Exception != null || resolving.IsCanceled || resolving.Result.Length == 0)
                {
                    return ResolveStatus.Failed;
                }

                addresses = IPv4First(resolving.Result);
            }

            resolved = addresses;
            return ResolveStatus.Resolved;
        }

        private static IPAddress[] IPv4First(IPAddress[] unordered)
        {
            var ordered = new List<IPAddress>(unordered.Length);
            foreach (IPAddress address in unordered)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    ordered.Add(address);
                }
            }

            foreach (IPAddress address in unordered)
            {
                if (address.AddressFamily != AddressFamily.InterNetwork)
                {
                    ordered.Add(address);
                }
            }

            return ordered.ToArray();
        }
    }
}
