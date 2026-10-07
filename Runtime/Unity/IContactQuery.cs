using System.Collections.Generic;
using UnityEngine;

namespace Fomoxa.Unity
{
    public interface IContactQuery
    {
        void Collect(Collider own, HashSet<Collider> into);
    }

    public interface IContactQuery2D
    {
        void Collect(Collider2D own, HashSet<Collider2D> into);
    }
}
