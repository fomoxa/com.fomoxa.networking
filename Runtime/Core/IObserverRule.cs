namespace Fomoxa.Networking
{
    public interface IObserverRule
    {
        bool RebuildsOnFirstAnchor { get; }

        bool Observes(ObserverContext context, INetworkEntity entity, ulong peerId);
    }
}
