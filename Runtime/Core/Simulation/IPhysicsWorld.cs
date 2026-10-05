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

        bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit);

        int Overlap(Vector3 center, float radius, BodyHandle[] results);
    }
}
