using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class NetworkCollision : MonoBehaviour, IContactSource<Collider>
    {
        [SerializeField] private float additionalSize = -1f;

        private readonly List<Collider> colliders = new List<Collider>();
        private readonly ContactSet<Collider> contacts = new ContactSet<Collider>();
        private PhysicsScene joined;
        private bool joinedAny;
        private ContactTracker<Collider> joinedTracker;
        private ContactTracker<Collider> attachedTracker;
        private IContactQuery attachedQuery;
        private bool live;

        public event Action<Collider> OnEnter;

        public event Action<Collider> OnExit;

        public IReadOnlyCollection<Collider> Touching => contacts.Published;

        public bool IsAttached => attachedQuery != null;

        public float AdditionalSize
        {
            get => additionalSize >= 0f ? additionalSize : Physics.defaultContactOffset * 2f;
            set => additionalSize = Mathf.Max(0f, value);
        }

        public bool Attach(ContactTracker<Collider> tracker, IContactQuery query)
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

        public void Detach(IContactQuery query)
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

        ContactSet<Collider> IContactSource<Collider>.Contacts => contacts;

        bool IContactSource<Collider>.MovedWorld => attachedQuery == null && gameObject.scene.GetPhysicsScene() != joined;

        void IContactSource<Collider>.Collect(HashSet<Collider> into)
        {
            if (attachedQuery != null)
            {
                ContactQueries.Collect(colliders, false, attachedQuery, into);
            }
            else
            {
                ContactQueries.Collect(colliders, false, AdditionalSize, into);
            }
        }

        void IContactSource<Collider>.Rejoin()
        {
            Leave();
            Join();
        }

        void IContactSource<Collider>.RaiseEnter(Collider other) => Raise(OnEnter, other);

        void IContactSource<Collider>.RaiseExit(Collider other) => Raise(OnExit, other);

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

        private void OnCollisionEnter(Collision collision)
        {
            if (attachedQuery == null && !PhysicsStepOwners.Worlds.Owns(gameObject.scene.GetPhysicsScene()))
            {
                contacts.PassEnter(this, collision.collider);
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            if (attachedQuery == null && !PhysicsStepOwners.Worlds.Owns(gameObject.scene.GetPhysicsScene()))
            {
                contacts.PassExit(this, collision.collider);
            }
        }

        private void Rehome(ContactTracker<Collider> tracker, IContactQuery query)
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

            joined = gameObject.scene.GetPhysicsScene();
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

        private static void Raise(Action<Collider> handler, Collider other)
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
