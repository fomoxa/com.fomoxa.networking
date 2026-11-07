using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Networking.Simulation
{
    public readonly struct PhysicsBody
    {
        private readonly IPhysicsWorld world;
        private readonly BodyHandle handle;

        public PhysicsBody(IPhysicsWorld world, BodyHandle handle)
        {
            this.world = world;
            this.handle = handle;
        }

        public BodyHandle Handle => handle;

        public bool IsValid => world != null && handle.IsValid && world.Contains(handle);

        public Vector3 Position
        {
            get => world.GetBody(handle).Position;
            set
            {
                BodyState state = world.GetBody(handle);
                state.Position = value;
                world.SetBody(handle, state);
            }
        }

        public Quaternion Rotation
        {
            get => world.GetBody(handle).Rotation;
            set
            {
                BodyState state = world.GetBody(handle);
                state.Rotation = value;
                world.SetBody(handle, state);
            }
        }

        public Vector3 Velocity
        {
            get => world.GetBody(handle).Velocity;
            set
            {
                BodyState state = world.GetBody(handle);
                state.Velocity = value;
                world.SetBody(handle, state);
            }
        }

        public Vector3 AngularVelocity
        {
            get => world.GetBody(handle).AngularVelocity;
            set
            {
                BodyState state = world.GetBody(handle);
                state.AngularVelocity = value;
                world.SetBody(handle, state);
            }
        }

        public float Mass => world.GetMass(handle);

        public bool IsKinematic => world.GetKind(handle) == BodyKind.Kinematic;

        public BodyState State
        {
            get => world.GetBody(handle);
            set => world.SetBody(handle, value);
        }

        public void AddForce(Vector3 force) => world.AddForce(handle, force);

        public void AddImpulse(Vector3 impulse) => world.AddImpulse(handle, impulse);

        public void AddTorque(Vector3 torque) => world.AddTorque(handle, torque);

        public void AddTorqueImpulse(Vector3 torqueImpulse) => world.AddTorqueImpulse(handle, torqueImpulse);

        public void AddForceAtPoint(Vector3 force, Vector3 point) => world.AddForceAtPoint(handle, force, point);

        public void AddImpulseAtPoint(Vector3 impulse, Vector3 point) => world.AddImpulseAtPoint(handle, impulse, point);

        public int GetContactPoints(BodyHandle other, List<ContactPointInfo> into) => world.GetContactPoints(handle, other, QueryFilter.All, into);

        public int GetContactPoints(BodyHandle other, in QueryFilter filter, List<ContactPointInfo> into) => world.GetContactPoints(handle, other, filter, into);
    }
}
