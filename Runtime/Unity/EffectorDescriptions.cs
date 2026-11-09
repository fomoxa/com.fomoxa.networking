using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using UnityEngine;

namespace Fomoxa.Unity
{
    public static class EffectorDescriptions
    {
        public const byte Platform = 1;
        public const byte Surface = 2;
        public const byte Area = 3;
        public const byte Buoyancy = 4;
        public const byte Point = 5;

        public static bool TryFind(Collider2D collider, out Effector2D effector)
        {
            if (collider == null)
            {
                throw new ArgumentNullException(nameof(collider));
            }

            if (collider.usedByEffector && collider.TryGetComponent(out effector) && effector.enabled)
            {
                return true;
            }

            effector = null;
            return false;
        }

        public static SceneFileEffector2D Describe(Effector2D effector, Transform owner, List<string> ignored)
        {
            if (effector == null)
            {
                throw new ArgumentNullException(nameof(effector));
            }

            if (ignored == null)
            {
                throw new ArgumentNullException(nameof(ignored));
            }

            Transform transform = effector.transform;
            Vector3 origin = owner == null ? transform.position : Quaternion.Inverse(owner.rotation) * (transform.position - owner.position);
            float ownerAngle = owner == null ? 0f : owner.eulerAngles.z;
            var file = new SceneFileEffector2D
            {
                UseColliderMask = effector.useColliderMask,
                ColliderMask = (uint)effector.colliderMask,
                Rotation = Mathf.DeltaAngle(ownerAngle, transform.eulerAngles.z) * Mathf.Deg2Rad,
                OriginX = origin.x,
                OriginY = origin.y,
            };
            switch (effector)
            {
                case PlatformEffector2D platform:
                    file.Kind = Platform;
                    file.OneWay = platform.useOneWay;
                    file.OneWayGrouping = platform.useOneWayGrouping;
                    file.RotationalOffset = platform.rotationalOffset * Mathf.Deg2Rad;
                    file.SurfaceArc = platform.surfaceArc * Mathf.Deg2Rad;
                    file.SideFriction = platform.useSideFriction;
                    file.SideBounce = platform.useSideBounce;
                    file.SideArc = platform.sideArc * Mathf.Deg2Rad;
                    break;
                case SurfaceEffector2D surface:
                    file.Kind = Surface;
                    file.Speed = surface.speed;
                    file.ForceScale = surface.forceScale;
                    file.ContactForce = surface.useContactForce;
                    file.Friction = surface.useFriction;
                    file.Bounce = surface.useBounce;
                    Ignore(surface.speedVariation, "speedVariation", ignored);
                    break;
                case AreaEffector2D area:
                    file.Kind = Area;
                    file.ForceAngle = area.forceAngle * Mathf.Deg2Rad;
                    file.GlobalAngle = area.useGlobalAngle;
                    file.ForceMagnitude = area.forceMagnitude;
                    file.ForceTarget = (byte)area.forceTarget;
                    file.LinearDamping = area.linearDamping;
                    file.AngularDamping = area.angularDamping;
                    Ignore(area.forceVariation, "forceVariation", ignored);
                    break;
                case BuoyancyEffector2D buoyancy:
                    file.Kind = Buoyancy;
                    file.SurfaceLevel = buoyancy.surfaceLevel * transform.lossyScale.y;
                    file.Density = buoyancy.density;
                    file.LinearDamping = buoyancy.linearDamping;
                    file.AngularDamping = buoyancy.angularDamping;
                    file.FlowAngle = buoyancy.flowAngle * Mathf.Deg2Rad;
                    file.FlowMagnitude = buoyancy.flowMagnitude;
                    Ignore(buoyancy.flowVariation, "flowVariation", ignored);
                    break;
                case PointEffector2D point:
                    file.Kind = Point;
                    file.ForceMagnitude = point.forceMagnitude;
                    file.DistanceScale = point.distanceScale;
                    file.ForceSource = (byte)point.forceSource;
                    file.ForceTarget = (byte)point.forceTarget;
                    file.ForceMode = (byte)point.forceMode;
                    file.LinearDamping = point.linearDamping;
                    file.AngularDamping = point.angularDamping;
                    Ignore(point.forceVariation, "forceVariation", ignored);
                    break;
                default:
                    throw new NotSupportedException($"{effector.GetType().Name} on {effector.name} is not a 2D effector the scene file describes");
            }

            return file;
        }

        private static void Ignore(float variation, string name, List<string> ignored)
        {
            if (variation != 0f)
            {
                ignored.Add(name);
            }
        }
    }
}
