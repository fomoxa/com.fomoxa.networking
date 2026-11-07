using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Networking.Simulation
{
    public abstract class PhysicsSnapshot
    {
    }

    public interface IPhysicsSimulation
    {
        PhysicsBackend Backend { get; }

        void Step(float seconds);

        PhysicsSnapshot CreateSnapshot();

        void Save(PhysicsSnapshot into);

        void Load(PhysicsSnapshot from);
    }

    public interface IPhysicsWorld : IPhysicsSimulation
    {
        BodyHandle CreateBody(in BodyDesc desc);

        bool RemoveBody(BodyHandle body);

        bool Contains(BodyHandle body);

        BodyState GetBody(BodyHandle body);

        BodyKind GetKind(BodyHandle body);

        float GetMass(BodyHandle body);

        void SetBody(BodyHandle body, in BodyState state);

        void SetRewindable(BodyHandle body, bool rewindable);

        void AddForce(BodyHandle body, Vector3 force);

        void AddImpulse(BodyHandle body, Vector3 impulse);

        void AddTorque(BodyHandle body, Vector3 torque);

        void AddTorqueImpulse(BodyHandle body, Vector3 torqueImpulse);

        void AddForceAtPoint(BodyHandle body, Vector3 force, Vector3 point);

        void AddImpulseAtPoint(BodyHandle body, Vector3 impulse, Vector3 point);

        bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, in QueryFilter filter, out RayHit hit);

        bool ShapeCast(in BodyShape shape, Vector3 position, Quaternion rotation, Vector3 direction, float maxDistance, in QueryFilter filter, out RayHit hit);

        int Overlap(in BodyShape shape, Vector3 position, Quaternion rotation, in QueryFilter filter, BodyHandle[] results);

        int GetContactPoints(BodyHandle body, BodyHandle other, in QueryFilter filter, List<ContactPointInfo> into);
    }
}
