using Fomoxa.Networking;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class MarkedStateBehaviour : NetworkBehaviour
    {
        public CounterState State { get; } = new CounterState();

        public void MarkChanged() => SetStateDirty();

        protected override void OnRegisterState(NetworkState state)
        {
            state.Use(StateCodecs.Counter, State, onlyWhenDirty: true);
        }
    }
}
