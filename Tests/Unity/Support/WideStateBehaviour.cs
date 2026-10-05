using System.Collections.Generic;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class WideStateBehaviour : NetworkBehaviour
    {
        public readonly List<string> Changes = new List<string>();

        public WideState State { get; } = new WideState();

        public string ValuesAtStartClient { get; private set; }

        public static string Describe(WideState state) => string.Join(",", state.Values);

        public override void OnStartClient()
        {
            ValuesAtStartClient = Describe(State);
        }

        protected override void OnRegisterState(NetworkState state)
        {
            state.Use(WideStateCodec.Instance, State, previous => Changes.Add($"{Describe(previous)} -> {Describe(State)}"));
        }
    }
}
