using System;
using System.Collections.Generic;
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

        public event Action<Collider> OnEnter;

        public event Action<Collider> OnExit;

        public IReadOnlyCollection<Collider> Touching => contacts.Published;

        public float AdditionalSize
        {
            get => additionalSize >= 0f ? additionalSize : Physics.defaultContactOffset * 2f;
            set => additionalSize = Mathf.Max(0f, value);
        }

        ContactSet<Collider> IContactSource<Collider>.Contacts => contacts;

        bool IContactSource<Collider>.MovedWorld => gameObject.scene.GetPhysicsScene() != joined;

        void IContactSource<Collider>.Collect(HashSet<Collider> into) => ContactQueries.Collect(colliders, false, AdditionalSize, into);

        void IContactSource<Collider>.Rejoin()
        {
            Leave();
            Join();
        }

        void IContactSource<Collider>.RaiseEnter(Collider other) => Raise(OnEnter, other);

        void IContactSource<Collider>.RaiseExit(Collider other) => Raise(OnExit, other);

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

        private void OnCollisionEnter(Collision collision)
        {
            if (!PhysicsStepOwners.Worlds.Owns(gameObject.scene.GetPhysicsScene()))
            {
                contacts.PassEnter(this, collision.collider);
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            if (!PhysicsStepOwners.Worlds.Owns(gameObject.scene.GetPhysicsScene()))
            {
                contacts.PassExit(this, collision.collider);
            }
        }

        private void Join()
        {
            joined = gameObject.scene.GetPhysicsScene();
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
