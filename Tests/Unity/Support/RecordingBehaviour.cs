using System;
using System.Collections.Generic;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class RecordingBehaviour : NetworkBehaviour
    {
        public static readonly List<string> Log = new List<string>();

        public static string ThrowOn { get; set; }

        public static void Clear()
        {
            Log.Clear();
            ThrowOn = null;
        }

        public override void OnStartServer() => Record("StartServer");

        public override void OnStopServer() => Record("StopServer");

        public override void OnStartClient() => Record("StartClient");

        public override void OnStopClient() => Record("StopClient");

        public override void OnOwnerChangedServer(ulong previousOwnerId) => Record("OwnerChangedServer", " " + previousOwnerId);

        public override void OnOwnerChangedClient(ulong previousOwnerId) => Record("OwnerChangedClient", " " + previousOwnerId);

        protected override void OnHostVisibility(bool visible) => Record("HostVisibility", " " + visible);

        private void Record(string call, string detail = "")
        {
            Log.Add($"{NetworkObject.ObjectId} {call}{detail}");
            if (call == ThrowOn)
            {
                throw new InvalidOperationException($"{call} failed");
            }
        }
    }
}
