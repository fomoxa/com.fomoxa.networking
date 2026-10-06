using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class NetworkCollision2D : MonoBehaviour, IContactSource<Collider2D>
    {
        private readonly List<Collider2D> colliders = new List<Collider2D>();
        private readonly ContactSet<Collider2D> contacts = new ContactSet<Collider2D>();
        private PhysicsScene2D joined;
        private bool joinedAny;

        public event Action<Collider2D> OnEnter;

        public event Action<Collider2D> OnExit;

        public IReadOnlyCollection<Collider2D> Touching => contacts.Published;

        ContactSet<Collider2D> IContactSource<Collider2D>.Contacts => contacts;

        bool IContactSource<Collider2D>.MovedWorld => gameObject.scene.GetPhysicsScene2D() != joined;

        void IContactSource<Collider2D>.Collect(HashSet<Collider2D> into) => ContactQueries.Collect(colliders, false, into);

        void IContactSource<Collider2D>.Rejoin()
        {
            Leave();
            Join();
        }

        void IContactSource<Collider2D>.RaiseEnter(Collider2D other) => Raise(OnEnter, other);

        void IContactSource<Collider2D>.RaiseExit(Collider2D other) => Raise(OnExit, other);

        private void OnEnable()
        {
            GetComponents(colliders);
            Join();
        }

        private void OnDisable()
        {
            Leave();
            contacts.Clear();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!PhysicsStepOwners.Worlds2D.Owns(gameObject.scene.GetPhysicsScene2D()))
            {
                contacts.PassEnter(this, collision.collider);
            }
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (!PhysicsStepOwners.Worlds2D.Owns(gameObject.scene.GetPhysicsScene2D()))
            {
                contacts.PassExit(this, collision.collider);
            }
        }

        private void Join()
        {
            joined = gameObject.scene.GetPhysicsScene2D();
            joinedAny = true;
            ContactTrackers.Join(joined, this);
        }

        private void Leave()
        {
            if (joinedAny)
            {
                ContactTrackers.Leave(joined, this);
                joinedAny = false;
            }
        }

        private static void Raise(Action<Collider2D> handler, Collider2D other)
        {
            try
            {
                handler?.Invoke(other);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
