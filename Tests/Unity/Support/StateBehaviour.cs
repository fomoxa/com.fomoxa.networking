using System.Collections.Generic;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class StateBehaviour : NetworkBehaviour
    {
        public readonly List<string> Changes = new List<string>();

        public CounterState State { get; } = new CounterState();

        public uint ValueAtStartClient { get; private set; } = uint.MaxValue;

        public override void OnStartClient()
        {
            ValueAtStartClient = State.Value;
        }

        protected override void OnRegisterState(NetworkState state)
        {
            state.Use(StateCodecs.Counter, State, previous => Changes.Add($"{previous.Value}->{State.Value}"));
        }
    }
}
