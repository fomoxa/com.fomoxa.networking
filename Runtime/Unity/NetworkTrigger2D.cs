using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class NetworkTrigger2D : MonoBehaviour, IContactSource<Collider2D>
    {
        private readonly List<Collider2D> colliders = new List<Collider2D>();
        private readonly ContactSet<Collider2D> contacts = new ContactSet<Collider2D>();
        private PhysicsScene2D joined;
        private bool joinedAny;
        private ContactTracker<Collider2D> joinedTracker;
        private ContactTracker<Collider2D> attachedTracker;
        private IContactQuery2D attachedQuery;
        private bool live;

        public event Action<Collider2D> OnEnter;

        public event Action<Collider2D> OnExit;

        public IReadOnlyCollection<Collider2D> Touching => contacts.Published;

        public bool IsAttached => attachedQuery != null;

        public bool Attach(ContactTracker<Collider2D> tracker, IContactQuery2D query)
        {
            if (tracker == null)
            {
                throw new ArgumentNullException(nameof(tracker));
            }

            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            if (attachedQuery != null)
            {
                return false;
            }

            Rehome(tracker, query);
            return true;
        }

        public void Detach(IContactQuery2D query)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            if (attachedQuery == query)
            {
                Rehome(null, null);
            }
        }

        ContactSet<Collider2D> IContactSource<Collider2D>.Contacts => contacts;

        bool IContactSource<Collider2D>.MovedWorld => attachedQuery == null && gameObject.scene.GetPhysicsScene2D() != joined;

        void IContactSource<Collider2D>.Collect(HashSet<Collider2D> into)
        {
            if (attachedQuery != null)
            {
                ContactQueries.Collect(colliders, true, attachedQuery, into);
            }
            else
            {
                ContactQueries.Collect(colliders, true, into);
            }
        }

        void IContactSource<Collider2D>.Rejoin()
        {
            Leave();
            Join();
        }

        void IContactSource<Collider2D>.RaiseEnter(Collider2D other) => Raise(OnEnter, other);

        void IContactSource<Collider2D>.RaiseExit(Collider2D other) => Raise(OnExit, other);

        private void OnEnable()
        {
            live = true;
            GetComponents(colliders);
            Join();
        }

        private void OnDisable()
        {
            live = false;
            Leave();
            contacts.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (attachedQuery == null && !PhysicsStepOwners.Worlds2D.Owns(gameObject.scene.GetPhysicsScene2D()))
            {
                contacts.PassEnter(this, other);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (attachedQuery == null && !PhysicsStepOwners.Worlds2D.Owns(gameObject.scene.GetPhysicsScene2D()))
            {
                contacts.PassExit(this, other);
            }
        }

        private void Rehome(ContactTracker<Collider2D> tracker, IContactQuery2D query)
        {
            Leave();
            contacts.Clear();
            attachedTracker = tracker;
            attachedQuery = query;
            if (live)
            {
                Join();
            }
        }

        private void Join()
        {
            joinedAny = true;
            if (attachedTracker != null)
            {
                joinedTracker = attachedTracker;
                joinedTracker.Add(this);
                return;
            }

            joined = gameObject.scene.GetPhysicsScene2D();
            ContactTrackers.Join(joined, this);
        }

        private void Leave()
        {
            if (!joinedAny)
            {
                return;
            }

            joinedAny = false;
            if (joinedTracker != null)
            {
                joinedTracker.Remove(this);
                joinedTracker = null;
            }
            else
            {
                ContactTrackers.Leave(joined, this);
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
