using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Networking.Simulation
{
    public interface IPhysicsWorld2D : IPhysicsSimulation
    {
        BodyHandle CreateBody(in BodyDesc2D desc);

        bool RemoveBody(BodyHandle body);

        bool Contains(BodyHandle body);

        BodyState2D GetBody(BodyHandle body);

        BodyKind GetKind(BodyHandle body);

        float GetMass(BodyHandle body);

        void SetBody(BodyHandle body, in BodyState2D state);

        void SetRewindable(BodyHandle body, bool rewindable);

        void AddForce(BodyHandle body, Vector2 force);

        void AddImpulse(BodyHandle body, Vector2 impulse);

        void AddTorque(BodyHandle body, float torque);

        void AddTorqueImpulse(BodyHandle body, float torqueImpulse);

        void AddForceAtPoint(BodyHandle body, Vector2 force, Vector2 point);

        void AddImpulseAtPoint(BodyHandle body, Vector2 impulse, Vector2 point);

        bool Raycast(Vector2 origin, Vector2 direction, float maxDistance, in QueryFilter filter, out RayHit2D hit);

        bool ShapeCast(in BodyShape2D shape, Vector2 position, float rotation, Vector2 direction, float maxDistance, in QueryFilter filter, out RayHit2D hit);

        int Overlap(in BodyShape2D shape, Vector2 position, float rotation, in QueryFilter filter, BodyHandle[] results);

        int GetContactPoints(BodyHandle body, BodyHandle other, in QueryFilter filter, List<ContactPointInfo2D> into);
    }
}
