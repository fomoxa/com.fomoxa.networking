using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fomoxa.Unity
{
    internal static class ContactQueries
    {
        private static readonly List<Collider2D> contacts2D = new List<Collider2D>();
        private static readonly HashSet<Collider> queried = new HashSet<Collider>();
        private static readonly HashSet<Collider2D> queried2D = new HashSet<Collider2D>();
        private static Collider[] overlapBuffer = new Collider[32];

        public static void Collect(List<Collider> own, bool triggers, float additionalSize, HashSet<Collider> into)
        {
            QueryTriggerInteraction interaction = triggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
            foreach (Collider tracked in own)
            {
                if (tracked == null || !tracked.enabled || !tracked.gameObject.activeInHierarchy || tracked.isTrigger != triggers)
                {
                    continue;
                }

                PhysicsScene physicsScene = tracked.gameObject.scene.GetPhysicsScene();
                int layers = CollidingLayers(tracked.gameObject.layer);
                int count;
                while ((count = Overlap(physicsScene, tracked, additionalSize, layers, interaction)) == overlapBuffer.Length)
                {
                    overlapBuffer = new Collider[overlapBuffer.Length * 2];
                }

                for (int index = 0; index < count; index++)
                {
                    Collider other = overlapBuffer[index];
                    if (!own.Contains(other) && !SameBody(tracked, other) && !Physics.GetIgnoreCollision(tracked, other))
                    {
                        into.Add(other);
                    }
                }

                Array.Clear(overlapBuffer, 0, count);
            }
        }

        public static void Collect(List<Collider2D> own, bool triggers, HashSet<Collider2D> into)
        {
            ContactFilter2D filter = triggers ? new ContactFilter2D().NoFilter() : new ContactFilter2D { useTriggers = false };
            foreach (Collider2D tracked in own)
            {
                if (tracked == null || !tracked.enabled || !tracked.gameObject.activeInHierarchy || tracked.isTrigger != triggers)
                {
                    continue;
                }

                contacts2D.Clear();
                tracked.GetContacts(filter, contacts2D);
                foreach (Collider2D other in contacts2D)
                {
                    if (!own.Contains(other))
                    {
                        into.Add(other);
                    }
                }
            }

            contacts2D.Clear();
        }

        public static void Collect(List<Collider> own, bool triggers, IContactQuery query, HashSet<Collider> into)
        {
            foreach (Collider tracked in own)
            {
                if (tracked == null || !tracked.enabled || !tracked.gameObject.activeInHierarchy || tracked.isTrigger != triggers)
                {
                    continue;
                }

                queried.Clear();
                query.Collect(tracked, queried);
                foreach (Collider other in queried)
                {
                    if (!own.Contains(other))
                    {
                        into.Add(other);
                    }
                }
            }

            queried.Clear();
        }

        public static void Collect(List<Collider2D> own, bool triggers, IContactQuery2D query, HashSet<Collider2D> into)
        {
            foreach (Collider2D tracked in own)
            {
                if (tracked == null || !tracked.enabled || !tracked.gameObject.activeInHierarchy || tracked.isTrigger != triggers)
                {
                    continue;
                }

                queried2D.Clear();
                query.Collect(tracked, queried2D);
                foreach (Collider2D other in queried2D)
                {
                    if (!own.Contains(other))
                    {
                        into.Add(other);
                    }
                }
            }

            queried2D.Clear();
        }

        private static int Overlap(PhysicsScene physicsScene, Collider tracked, float additionalSize, int layers, QueryTriggerInteraction interaction)
        {
            Transform placed = tracked.transform;
            Vector3 scale = Abs(placed.lossyScale);
            switch (tracked)
            {
                case BoxCollider box:
                    Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, scale) + (Vector3.one * additionalSize);
                    return physicsScene.OverlapBox(placed.TransformPoint(box.center), halfExtents, overlapBuffer, placed.rotation, layers, interaction);
                case SphereCollider sphere:
                    float radius = (sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z))) + additionalSize;
                    return physicsScene.OverlapSphere(placed.TransformPoint(sphere.center), radius, overlapBuffer, layers, interaction);
                case CapsuleCollider capsule:
                    Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                    float axisScale = Vector3.Scale(axis, scale).magnitude;
                    float sideScale = capsule.direction == 0 ? Mathf.Max(scale.y, scale.z) : capsule.direction == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);
                    float capsuleRadius = capsule.radius * sideScale;
                    float halfSegment = Mathf.Max((capsule.height * axisScale * 0.5f) - capsuleRadius, 0f);
                    Vector3 center = placed.TransformPoint(capsule.center);
                    Vector3 offset = placed.rotation * axis * halfSegment;
                    return physicsScene.OverlapCapsule(center + offset, center - offset, capsuleRadius + additionalSize, overlapBuffer, layers, interaction);
                default:
                    Bounds bounds = tracked.bounds;
                    return physicsScene.OverlapBox(bounds.center, bounds.extents + (Vector3.one * additionalSize), overlapBuffer, Quaternion.identity, layers, interaction);
            }
        }

        private static int CollidingLayers(int layer)
        {
            int layers = 0;
            for (int other = 0; other < 32; other++)
            {
                if (!Physics.GetIgnoreLayerCollision(layer, other))
                {
                    layers |= 1 << other;
                }
            }

            return layers;
        }

        private static bool SameBody(Collider tracked, Collider other) =>
            tracked.attachedRigidbody != null && tracked.attachedRigidbody == other.attachedRigidbody;

        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
