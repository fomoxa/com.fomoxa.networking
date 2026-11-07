using System.Collections.Generic;
using UnityEngine;

namespace Fomoxa.Unity
{
    public interface IContactQuery
    {
        void Collect(Collider own, HashSet<Collider> into);

        void CollectContactPoints(Collider own, Collider other, List<NetworkContactPoint> into);
    }

    public interface IContactQuery2D
    {
        void Collect(Collider2D own, HashSet<Collider2D> into);

        void CollectContactPoints(Collider2D own, Collider2D other, List<NetworkContactPoint2D> into);
    }
}
