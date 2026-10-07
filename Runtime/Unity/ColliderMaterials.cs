using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;

namespace Fomoxa.Unity
{
    internal static class ColliderMaterials
    {
        private static readonly Dictionary<ColliderMaterial, PhysicsMaterial> Materials = new Dictionary<ColliderMaterial, PhysicsMaterial>();
        private static readonly Dictionary<ColliderMaterial, PhysicsMaterial2D> Materials2D = new Dictionary<ColliderMaterial, PhysicsMaterial2D>();
        private static ColliderMaterial? unityDefault;
        private static ColliderMaterial? unityDefault2D;

        public static void Check(in ColliderMaterial material)
        {
            ToUnity(material.FrictionCombine);
            ToUnity(material.RestitutionCombine);
        }

        public static PhysicsMaterial ToUnity(in ColliderMaterial material)
        {
            if (Materials.TryGetValue(material, out PhysicsMaterial cached) && cached != null)
            {
                return cached;
            }

            var created = new PhysicsMaterial("FomoxaMaterial")
            {
                dynamicFriction = material.Friction,
                staticFriction = material.Friction,
                bounciness = material.Restitution,
                frictionCombine = ToUnity(material.FrictionCombine),
                bounceCombine = ToUnity(material.RestitutionCombine),
            };
            Materials[material] = created;
            return created;
        }

        public static PhysicsMaterial2D ToUnity2D(in ColliderMaterial material)
        {
            if (Materials2D.TryGetValue(material, out PhysicsMaterial2D cached) && cached != null)
            {
                return cached;
            }

            var created = new PhysicsMaterial2D("FomoxaMaterial2D")
            {
                friction = material.Friction,
                bounciness = material.Restitution,
                frictionCombine = ToUnity2D(material.FrictionCombine),
                bounceCombine = ToUnity2D(material.RestitutionCombine),
            };
            Materials2D[material] = created;
            return created;
        }

        public static ColliderMaterial FromUnity(PhysicsMaterial material)
        {
            if (material == null)
            {
                return unityDefault ??= ReadDefault();
            }

            return new ColliderMaterial(material.dynamicFriction, material.bounciness, FromUnity(material.frictionCombine), FromUnity(material.bounceCombine));
        }

        public static ColliderMaterial FromUnity(PhysicsMaterial2D material)
        {
            if (material == null)
            {
                return unityDefault2D ??= ReadDefault2D();
            }

            return new ColliderMaterial(material.friction, material.bounciness, FromUnity(material.frictionCombine), FromUnity(material.bounceCombine));
        }

        private static ColliderMaterial ReadDefault()
        {
            var probe = new PhysicsMaterial();
            try
            {
                return FromUnity(probe);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        private static ColliderMaterial ReadDefault2D()
        {
            var probe = new PhysicsMaterial2D();
            try
            {
                return FromUnity(probe);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        private static PhysicsMaterialCombine ToUnity(CombineRule rule)
        {
            switch (rule)
            {
                case CombineRule.Average:
                    return PhysicsMaterialCombine.Average;
                case CombineRule.Minimum:
                    return PhysicsMaterialCombine.Minimum;
                case CombineRule.Multiply:
                    return PhysicsMaterialCombine.Multiply;
                case CombineRule.Maximum:
                    return PhysicsMaterialCombine.Maximum;
                default:
                    throw new NotSupportedException($"3D physics of Unity has no {rule} combine rule");
            }
        }

        private static PhysicsMaterialCombine2D ToUnity2D(CombineRule rule)
        {
            switch (rule)
            {
                case CombineRule.Average:
                    return PhysicsMaterialCombine2D.Average;
                case CombineRule.Minimum:
                    return PhysicsMaterialCombine2D.Minimum;
                case CombineRule.Multiply:
                    return PhysicsMaterialCombine2D.Multiply;
                case CombineRule.Maximum:
                    return PhysicsMaterialCombine2D.Maximum;
                default:
                    return PhysicsMaterialCombine2D.Mean;
            }
        }

        private static CombineRule FromUnity(PhysicsMaterialCombine rule)
        {
            switch (rule)
            {
                case PhysicsMaterialCombine.Minimum:
                    return CombineRule.Minimum;
                case PhysicsMaterialCombine.Multiply:
                    return CombineRule.Multiply;
                case PhysicsMaterialCombine.Maximum:
                    return CombineRule.Maximum;
                default:
                    return CombineRule.Average;
            }
        }

        private static CombineRule FromUnity(PhysicsMaterialCombine2D rule)
        {
            switch (rule)
            {
                case PhysicsMaterialCombine2D.Average:
                    return CombineRule.Average;
                case PhysicsMaterialCombine2D.Minimum:
                    return CombineRule.Minimum;
                case PhysicsMaterialCombine2D.Multiply:
                    return CombineRule.Multiply;
                case PhysicsMaterialCombine2D.Maximum:
                    return CombineRule.Maximum;
                default:
                    return CombineRule.Mean;
            }
        }
    }
}
