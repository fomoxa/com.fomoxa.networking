using System;
using System.Collections.Generic;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class TestObserverRule : ObserverRule
    {
        public readonly HashSet<ulong> Hidden = new HashSet<ulong>();

        public bool HideAll { get; set; }

        public bool Throws { get; set; }

        public override bool Observes(NetworkObject networkObject, ulong peerId)
        {
            if (Throws)
            {
                throw new InvalidOperationException("rule failed");
            }

            return !HideAll && !Hidden.Contains(peerId);
        }
    }
}
