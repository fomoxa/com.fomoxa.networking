using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Networking.Simulation
{
    public static class PhysicsQueries
    {
        public static bool Raycast(this IPhysicsWorld world, Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit) =>
            world.Raycast(origin, direction, maxDistance, QueryFilter.All, out hit);

        public static int Overlap(this IPhysicsWorld world, Vector3 center, float radius, BodyHandle[] results) =>
            world.Overlap(BodyShape.Sphere(radius), center, Quaternion.Identity, QueryFilter.All, results);

        public static int GetContactPoints(this IPhysicsWorld world, BodyHandle body, BodyHandle other, List<ContactPointInfo> into) =>
            world.GetContactPoints(body, other, QueryFilter.All, into);

        public static bool Raycast(this IPhysicsWorld2D world, Vector2 origin, Vector2 direction, float maxDistance, out RayHit2D hit) =>
            world.Raycast(origin, direction, maxDistance, QueryFilter.All, out hit);

        public static int Overlap(this IPhysicsWorld2D world, Vector2 center, float radius, BodyHandle[] results) =>
            world.Overlap(BodyShape2D.Circle(radius), center, 0f, QueryFilter.All, results);

        public static int GetContactPoints(this IPhysicsWorld2D world, BodyHandle body, BodyHandle other, List<ContactPointInfo2D> into) =>
            world.GetContactPoints(body, other, QueryFilter.All, into);
    }
}
