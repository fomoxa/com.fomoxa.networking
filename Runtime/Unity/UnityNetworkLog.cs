using Fomoxa.Networking;
using UnityEngine;

namespace Fomoxa.Unity
{
    internal static class UnityNetworkLog
    {
        public static readonly NetworkLog Instance = new NetworkLog(Debug.LogException, Debug.LogWarning);
    }
}
