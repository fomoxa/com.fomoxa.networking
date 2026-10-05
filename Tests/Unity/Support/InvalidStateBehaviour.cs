namespace Fomoxa.Unity.Tests.Support
{
    public enum InvalidStateUse
    {
        Unreliable,
        Empty,
        Twice,
        AlsoAnRpc,
    }

    public sealed class InvalidStateBehaviour : NetworkBehaviour
    {
        public InvalidStateUse Mode;

        private readonly CounterState state = new CounterState();

        protected override void OnRegisterRpcs(NetworkRpcs rpc)
        {
            if (Mode == InvalidStateUse.AlsoAnRpc)
            {
                rpc.OnServer<CounterState>(StateCodecs.Counter, (peerId, value) => { });
            }
        }

        protected override void OnRegisterState(NetworkState registration)
        {
            switch (Mode)
            {
                case InvalidStateUse.Unreliable:
                    registration.Use(StateCodecs.Unreliable, state);
                    break;
                case InvalidStateUse.Empty:
                    registration.Use(StateCodecs.Empty, state);
                    break;
                case InvalidStateUse.Twice:
                    registration.Use(StateCodecs.Counter, state);
                    registration.Use(StateCodecs.Counter, state);
                    break;
                default:
                    registration.Use(StateCodecs.Counter, state);
                    break;
            }
        }
    }
}
