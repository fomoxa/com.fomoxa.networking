using System.Collections.Generic;

namespace Fomoxa.Networking.Simulation
{
    public interface IContactSource<TContact>
    {
        ContactSet<TContact> Contacts { get; }

        bool MovedWorld { get; }

        void Collect(HashSet<TContact> into);

        void Rejoin();

        void RaiseEnter(TContact other);

        void RaiseExit(TContact other);
    }
}
