using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;

namespace Fomoxa.Unity
{
    internal static class ContactTrackers
    {
        private static readonly Dictionary<PhysicsScene, ContactTracker<Collider>> trackers = new Dictionary<PhysicsScene, ContactTracker<Collider>>();
        private static readonly Dictionary<PhysicsScene2D, ContactTracker<Collider2D>> trackers2D = new Dictionary<PhysicsScene2D, ContactTracker<Collider2D>>();

        public static int Count => trackers.Count + trackers2D.Count;

        public static ContactTracker<Collider> Of(PhysicsScene physicsScene) =>
            trackers.TryGetValue(physicsScene, out ContactTracker<Collider> tracker) ? tracker : null;

        public static ContactTracker<Collider2D> Of(PhysicsScene2D physicsScene) =>
            trackers2D.TryGetValue(physicsScene, out ContactTracker<Collider2D> tracker) ? tracker : null;

        public static void Join(PhysicsScene physicsScene, IContactSource<Collider> source)
        {
            if (!trackers.TryGetValue(physicsScene, out ContactTracker<Collider> tracker))
            {
                tracker = new ContactTracker<Collider>();
                trackers.Add(physicsScene, tracker);
            }

            tracker.Add(source);
        }

        public static void Join(PhysicsScene2D physicsScene, IContactSource<Collider2D> source)
        {
            if (!trackers2D.TryGetValue(physicsScene, out ContactTracker<Collider2D> tracker))
            {
                tracker = new ContactTracker<Collider2D>();
                trackers2D.Add(physicsScene, tracker);
            }

            tracker.Add(source);
        }

        public static void Leave(PhysicsScene physicsScene, IContactSource<Collider> source)
        {
            if (trackers.TryGetValue(physicsScene, out ContactTracker<Collider> tracker))
            {
                tracker.Remove(source);
                if (tracker.Count == 0)
                {
                    trackers.Remove(physicsScene);
                }
            }
        }

        public static void Leave(PhysicsScene2D physicsScene, IContactSource<Collider2D> source)
        {
            if (trackers2D.TryGetValue(physicsScene, out ContactTracker<Collider2D> tracker))
            {
                tracker.Remove(source);
                if (tracker.Count == 0)
                {
                    trackers2D.Remove(physicsScene);
                }
            }
        }
    }
}
