using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    public static class BodyDescriptions
    {
        private const float HalfSqrtTwo = 0.70710677f;
        private const float QuarterTurn = Mathf.PI * 0.5f;
        private static readonly Quaternion AxisYToX = new Quaternion(0f, 0f, -HalfSqrtTwo, HalfSqrtTwo);
        private static readonly Quaternion AxisYToZ = new Quaternion(HalfSqrtTwo, 0f, 0f, HalfSqrtTwo);

        public static bool TryDescribe(GameObject root, out BodyDesc desc) => Describe(root, out desc, null);

        public static bool TryDescribe(GameObject root, out BodyDesc desc, List<Collider> sources)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            return Describe(root, out desc, sources);
        }

        public static bool TryDescribe2D(GameObject root, out BodyDesc2D desc) => Describe2D(root, out desc, null);

        public static bool TryDescribe2D(GameObject root, out BodyDesc2D desc, List<Collider2D> sources)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            return Describe2D(root, out desc, sources);
        }

        public static void StaticColliders(Scene scene, List<Collider> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            if (!scene.IsValid())
            {
                throw new ArgumentException("the scene is not valid", nameof(scene));
            }

            into.Clear();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                AppendStatics(root, into);
            }
        }

        public static void StaticColliders(GameObject root, List<Collider> into)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            AppendStatics(root, into);
        }

        public static void StaticColliders2D(Scene scene, List<Collider2D> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            if (!scene.IsValid())
            {
                throw new ArgumentException("the scene is not valid", nameof(scene));
            }

            into.Clear();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                AppendStatics2D(root, into);
            }
        }

        public static void StaticColliders2D(GameObject root, List<Collider2D> into)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            AppendStatics2D(root, into);
        }

        public static void DescribeStatic(Collider collider, Transform space, List<ColliderDesc> into)
        {
            if (collider == null)
            {
                throw new ArgumentNullException(nameof(collider));
            }

            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            Append(collider, space == null ? Vector3.zero : space.position, space == null ? Quaternion.identity : Quaternion.Inverse(space.rotation), true, into);
        }

        public static void DescribeStatic2D(Collider2D collider, Transform space, List<ColliderDesc2D> into)
        {
            if (collider == null)
            {
                throw new ArgumentNullException(nameof(collider));
            }

            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            Append2D(collider, space == null ? Vector3.zero : space.position, space == null ? Quaternion.identity : Quaternion.Inverse(space.rotation), true, into);
        }

        private static void AppendStatics(GameObject root, List<Collider> into)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                if (IsStatic(collider, collider.enabled, collider.attachedRigidbody != null))
                {
                    into.Add(collider);
                }
            }
        }

        private static void AppendStatics2D(GameObject root, List<Collider2D> into)
        {
            foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>())
            {
                if (!IsStatic(collider, collider.enabled, collider.attachedRigidbody != null))
                {
                    continue;
                }

                int pieces = collider is PolygonCollider2D polygon ? polygon.pathCount : collider is BoxCollider2D box && IsAutoTiled(box) ? box.shapeCount : 1;
                for (int piece = 0; piece < pieces; piece++)
                {
                    into.Add(collider);
                }
            }
        }

        private static bool IsStatic(Component collider, bool enabled, bool attached) =>
            enabled && !attached && collider.gameObject.activeInHierarchy && collider.GetComponentInParent<NetworkObject>(true) == null;

        private static bool Describe(GameObject root, out BodyDesc desc, List<Collider> sources)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            sources?.Clear();

            root.TryGetComponent(out Rigidbody rigidbody);
            BodyKind kind = rigidbody == null ? BodyKind.Static : rigidbody.isKinematic ? BodyKind.Kinematic : BodyKind.Dynamic;
            Transform space = root.transform;
            var colliders = new List<ColliderDesc>();
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                if (collider.enabled && collider.attachedRigidbody == rigidbody)
                {
                    Append(collider, space.position, Quaternion.Inverse(space.rotation), kind == BodyKind.Static, colliders);
                    while (sources != null && sources.Count < colliders.Count)
                    {
                        sources.Add(collider);
                    }
                }
            }

            if (colliders.Count == 0)
            {
                desc = default;
                return false;
            }

            desc = new BodyDesc(kind, colliders, space.position.ToNumerics(), space.rotation.ToNumerics(), rigidbody == null ? 0f : rigidbody.mass, MotionOf(rigidbody));
            return true;
        }

        private static bool Describe2D(GameObject root, out BodyDesc2D desc, List<Collider2D> sources)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            sources?.Clear();

            root.TryGetComponent(out Rigidbody2D rigidbody);
            BodyKind kind = KindOf(rigidbody);
            Transform space = root.transform;
            var colliders = new List<ColliderDesc2D>();
            foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>())
            {
                if (collider.enabled && collider.attachedRigidbody == rigidbody)
                {
                    Append2D(collider, space.position, Quaternion.Inverse(space.rotation), kind == BodyKind.Static, colliders);
                    while (sources != null && sources.Count < colliders.Count)
                    {
                        sources.Add(collider);
                    }
                }
            }

            if (colliders.Count == 0)
            {
                desc = default;
                return false;
            }

            Vector3 position = space.position;
            desc = new BodyDesc2D(kind, colliders, new System.Numerics.Vector2(position.x, position.y), space.eulerAngles.z * Mathf.Deg2Rad, rigidbody == null ? 0f : rigidbody.mass, MotionOf(rigidbody));
            return true;
        }

        private static void Append(Collider collider, Vector3 origin, Quaternion inverse, bool staticBody, List<ColliderDesc> into)
        {
            if (collider.includeLayers.value != 0 || collider.excludeLayers.value != 0)
            {
                throw Unsupported(collider, "includeLayers or excludeLayers");
            }

            Transform transform = collider.transform;
            Vector3 scale = Abs(transform.lossyScale);
            Quaternion rotation = inverse * transform.rotation;
            ColliderMaterial material = ColliderMaterials.FromUnity(collider.sharedMaterial);
            int layer = collider.gameObject.layer;
            switch (collider)
            {
                case BoxCollider box:
                    into.Add(Describe(BodyShape.Box((Vector3.Scale(box.size, scale) * 0.5f).ToNumerics()), Local(transform.TransformPoint(box.center), origin, inverse), rotation, material, layer, collider.isTrigger));
                    break;
                case SphereCollider sphere:
                    into.Add(Describe(BodyShape.Sphere(sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z))), Local(transform.TransformPoint(sphere.center), origin, inverse), rotation, material, layer, collider.isTrigger));
                    break;
                case CapsuleCollider capsule:
                    int axis = capsule.direction;
                    float radial = axis == 0 ? Mathf.Max(scale.y, scale.z) : axis == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);
                    float radius = capsule.radius * radial;
                    float height = Mathf.Max(capsule.height * scale[axis], radius * 2f);
                    Quaternion toAxis = axis == 0 ? AxisYToX : axis == 2 ? AxisYToZ : Quaternion.identity;
                    into.Add(Describe(BodyShape.Capsule(radius, height * 0.5f - radius), Local(transform.TransformPoint(capsule.center), origin, inverse), rotation * toAxis, material, layer, collider.isTrigger));
                    break;
                case MeshCollider meshCollider:
                    System.Numerics.Vector3[] vertices = ScaledVertices(meshCollider, transform.lossyScale);
                    BodyShape shape;
                    if (meshCollider.convex)
                    {
                        shape = BodyShape.ConvexHull(vertices);
                    }
                    else if (staticBody)
                    {
                        shape = BodyShape.TriangleMesh(vertices, meshCollider.sharedMesh.triangles);
                    }
                    else
                    {
                        throw Unsupported(collider, "a non-convex mesh on a body that is not static");
                    }

                    into.Add(Describe(shape, Local(transform.position, origin, inverse), rotation, material, layer, collider.isTrigger));
                    break;
                default:
                    throw Unsupported(collider, collider.GetType().Name);
            }
        }

        private static void Append2D(Collider2D collider, Vector3 origin, Quaternion inverse, bool staticBody, List<ColliderDesc2D> into)
        {
            if (collider.includeLayers.value != 0 || collider.excludeLayers.value != 0)
            {
                throw Unsupported(collider, "includeLayers or excludeLayers");
            }

            if (collider.compositeOperation != Collider2D.CompositeOperation.None)
            {
                throw Unsupported(collider, "a composite operation");
            }

            Transform transform = collider.transform;
            Vector3 lossy = Abs(transform.lossyScale);
            var scale = new Vector2(lossy.x, lossy.y);
            float angle = (inverse * transform.rotation).eulerAngles.z * Mathf.Deg2Rad;
            ColliderMaterial material = ColliderMaterials.FromUnity(collider.sharedMaterial != null ? collider.sharedMaterial : collider.attachedRigidbody != null ? collider.attachedRigidbody.sharedMaterial : null);
            int layer = collider.gameObject.layer;
            switch (collider)
            {
                case BoxCollider2D box:
                    if (box.edgeRadius != 0f)
                    {
                        throw Unsupported(collider, "edgeRadius");
                    }

                    if (IsAutoTiled(box))
                    {
                        AppendBuiltShapes(box, origin, inverse, material, layer, into);
                        break;
                    }

                    into.Add(Describe2D(BodyShape2D.Box((Vector2.Scale(box.size, scale) * 0.5f).ToNumerics()), Local2D(transform.TransformPoint(box.offset), origin, inverse), angle, material, layer, collider.isTrigger));
                    break;
                case CircleCollider2D circle:
                    into.Add(Describe2D(BodyShape2D.Circle(circle.radius * Mathf.Max(scale.x, scale.y)), Local2D(transform.TransformPoint(circle.offset), origin, inverse), angle, material, layer, collider.isTrigger));
                    break;
                case CapsuleCollider2D capsule:
                    Vector2 size = Vector2.Scale(capsule.size, scale);
                    bool vertical = capsule.direction == CapsuleDirection2D.Vertical;
                    float radius = (vertical ? size.x : size.y) * 0.5f;
                    float halfHeight = Mathf.Max((vertical ? size.y : size.x) * 0.5f - radius, 0f);
                    into.Add(Describe2D(BodyShape2D.Capsule(radius, halfHeight), Local2D(transform.TransformPoint(capsule.offset), origin, inverse), vertical ? angle : angle + QuarterTurn, material, layer, collider.isTrigger));
                    break;
                case PolygonCollider2D polygon:
                    for (int path = 0; path < polygon.pathCount; path++)
                    {
                        List<System.Numerics.Vector2> points = LocalPoints(polygon.GetPath(path), polygon.offset, transform, origin, inverse);
                        if (staticBody)
                        {
                            List<System.Numerics.Vector2> outline = PolygonDecomposition.Outline(points);
                            outline.Add(outline[0]);
                            into.Add(Describe2D(BodyShape2D.Polyline(outline), System.Numerics.Vector2.Zero, 0f, material, layer, collider.isTrigger));
                        }
                        else
                        {
                            foreach (System.Numerics.Vector2[] piece in PolygonDecomposition.Split(points))
                            {
                                into.Add(Describe2D(BodyShape2D.ConvexPolygon(piece), System.Numerics.Vector2.Zero, 0f, material, layer, collider.isTrigger));
                            }
                        }
                    }

                    break;
                case EdgeCollider2D edge:
                    if (edge.edgeRadius != 0f)
                    {
                        throw Unsupported(collider, "edgeRadius");
                    }

                    if (!staticBody)
                    {
                        throw Unsupported(collider, "an edge on a body that is not static");
                    }

                    into.Add(Describe2D(BodyShape2D.Polyline(LocalPoints(edge.points, edge.offset, transform, origin, inverse)), System.Numerics.Vector2.Zero, 0f, material, layer, collider.isTrigger));
                    break;
                default:
                    throw Unsupported(collider, collider.GetType().Name);
            }
        }

        private static bool IsAutoTiled(BoxCollider2D box) =>
            box.autoTiling && box.TryGetComponent(out SpriteRenderer renderer) && renderer.drawMode != SpriteDrawMode.Simple;

        private static void AppendBuiltShapes(Collider2D collider, Vector3 origin, Quaternion inverse, ColliderMaterial material, int layer, List<ColliderDesc2D> into)
        {
            Physics2D.SyncTransforms();
            var shapes = new PhysicsShapeGroup2D();
            int count = collider.GetShapes(shapes);
            if (count == 0)
            {
                throw Unsupported(collider, "auto tiling while Unity has built no physics shape for it (inactive or disabled)");
            }

            Rigidbody2D rigidbody = collider.attachedRigidbody;
            for (int shapeIndex = 0; shapeIndex < count; shapeIndex++)
            {
                PhysicsShape2D shape = shapes.GetShape(shapeIndex);
                if (shape.shapeType != PhysicsShapeType2D.Polygon)
                {
                    throw Unsupported(collider, $"auto tiling that built a {shape.shapeType} shape");
                }

                var points = new System.Numerics.Vector2[shape.vertexCount];
                for (int vertex = 0; vertex < points.Length; vertex++)
                {
                    Vector2 point = shapes.GetShapeVertex(shapeIndex, vertex);
                    Vector3 world = rigidbody == null ? (Vector3)point : (Vector3)rigidbody.position + Quaternion.AngleAxis(rigidbody.rotation, Vector3.forward) * point;
                    points[vertex] = Local2D(world, origin, inverse);
                }

                into.Add(Describe2D(BodyShape2D.ConvexPolygon(points), System.Numerics.Vector2.Zero, 0f, material, layer, collider.isTrigger));
            }
        }

        private static BodyMotion MotionOf(Rigidbody rigidbody)
        {
            if (rigidbody == null)
            {
                return BodyMotion.Default;
            }

            RigidbodyConstraints constraints = rigidbody.constraints;
            BodyLocks locks = BodyLocks.None;
            locks |= (constraints & RigidbodyConstraints.FreezePositionX) != 0 ? BodyLocks.PositionX : BodyLocks.None;
            locks |= (constraints & RigidbodyConstraints.FreezePositionY) != 0 ? BodyLocks.PositionY : BodyLocks.None;
            locks |= (constraints & RigidbodyConstraints.FreezePositionZ) != 0 ? BodyLocks.PositionZ : BodyLocks.None;
            locks |= (constraints & RigidbodyConstraints.FreezeRotationX) != 0 ? BodyLocks.RotationX : BodyLocks.None;
            locks |= (constraints & RigidbodyConstraints.FreezeRotationY) != 0 ? BodyLocks.RotationY : BodyLocks.None;
            locks |= (constraints & RigidbodyConstraints.FreezeRotationZ) != 0 ? BodyLocks.RotationZ : BodyLocks.None;
            return new BodyMotion(locks, rigidbody.useGravity, rigidbody.linearDamping, rigidbody.angularDamping);
        }

        private static BodyMotion2D MotionOf(Rigidbody2D rigidbody)
        {
            if (rigidbody == null)
            {
                return BodyMotion2D.Default;
            }

            RigidbodyConstraints2D constraints = rigidbody.constraints;
            BodyLocks2D locks = BodyLocks2D.None;
            locks |= (constraints & RigidbodyConstraints2D.FreezePositionX) != 0 ? BodyLocks2D.PositionX : BodyLocks2D.None;
            locks |= (constraints & RigidbodyConstraints2D.FreezePositionY) != 0 ? BodyLocks2D.PositionY : BodyLocks2D.None;
            locks |= (constraints & RigidbodyConstraints2D.FreezeRotation) != 0 ? BodyLocks2D.Rotation : BodyLocks2D.None;
            return new BodyMotion2D(locks, rigidbody.gravityScale, rigidbody.linearDamping, rigidbody.angularDamping);
        }

        private static ColliderDesc Describe(BodyShape shape, Vector3 position, Quaternion rotation, ColliderMaterial material, int layer, bool isTrigger) =>
            new ColliderDesc(shape, position.ToNumerics(), rotation.ToNumerics(), material, layer, isTrigger);

        private static ColliderDesc2D Describe2D(BodyShape2D shape, System.Numerics.Vector2 position, float rotation, ColliderMaterial material, int layer, bool isTrigger) =>
            new ColliderDesc2D(shape, position, rotation, material, layer, isTrigger);

        private static BodyKind KindOf(Rigidbody2D rigidbody)
        {
            if (rigidbody == null)
            {
                return BodyKind.Static;
            }

            switch (rigidbody.bodyType)
            {
                case RigidbodyType2D.Dynamic:
                    return BodyKind.Dynamic;
                case RigidbodyType2D.Kinematic:
                    return BodyKind.Kinematic;
                default:
                    return BodyKind.Static;
            }
        }

        private static System.Numerics.Vector3[] ScaledVertices(MeshCollider collider, Vector3 scale)
        {
            Mesh mesh = collider.sharedMesh;
            if (mesh == null)
            {
                throw new ArgumentException($"the MeshCollider on {collider.name} has no mesh", nameof(collider));
            }

            if (!mesh.isReadable)
            {
                throw new ArgumentException($"the mesh {mesh.name} of the MeshCollider on {collider.name} is not readable; enable Read/Write in its import settings", nameof(collider));
            }

            Vector3[] source = mesh.vertices;
            var vertices = new System.Numerics.Vector3[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                vertices[index] = Vector3.Scale(source[index], scale).ToNumerics();
            }

            return vertices;
        }

        private static List<System.Numerics.Vector2> LocalPoints(Vector2[] points, Vector2 offset, Transform transform, Vector3 origin, Quaternion inverse)
        {
            var local = new List<System.Numerics.Vector2>(points.Length);
            foreach (Vector2 point in points)
            {
                local.Add(Local2D(transform.TransformPoint(point + offset), origin, inverse));
            }

            return local;
        }

        private static Vector3 Local(Vector3 world, Vector3 origin, Quaternion inverse) => inverse * (world - origin);

        private static System.Numerics.Vector2 Local2D(Vector3 world, Vector3 origin, Quaternion inverse)
        {
            Vector3 local = inverse * (world - origin);
            return new System.Numerics.Vector2(local.x, local.y);
        }

        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        private static NotSupportedException Unsupported(Component collider, string what) =>
            new NotSupportedException($"{collider.GetType().Name} on {collider.name}: {what} is not supported by physics backends that build bodies from descriptions");
    }
}
