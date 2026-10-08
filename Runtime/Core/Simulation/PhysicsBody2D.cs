using System.Numerics;

namespace Fomoxa.Networking.Simulation
{
    public readonly struct PhysicsBody2D
    {
        private readonly IPhysicsWorld2D world;
        private readonly BodyHandle handle;

        public PhysicsBody2D(IPhysicsWorld2D world, BodyHandle handle)
        {
            this.world = world;
            this.handle = handle;
        }

        public BodyHandle Handle => handle;

        public bool IsValid => world != null && handle.IsValid && world.Contains(handle);

        public Vector2 Position
        {
            get => world.GetBody(handle).Position;
            set
            {
                BodyState2D state = world.GetBody(handle);
                state.Position = value;
                world.SetBody(handle, state);
            }
        }

        public float Rotation
        {
            get => world.GetBody(handle).Rotation;
            set
            {
                BodyState2D state = world.GetBody(handle);
                state.Rotation = value;
                world.SetBody(handle, state);
            }
        }

        public Vector2 Velocity
        {
            get => world.GetBody(handle).Velocity;
            set
            {
                BodyState2D state = world.GetBody(handle);
                state.Velocity = value;
                world.SetBody(handle, state);
            }
        }

        public float AngularVelocity
        {
            get => world.GetBody(handle).AngularVelocity;
            set
            {
                BodyState2D state = world.GetBody(handle);
                state.AngularVelocity = value;
                world.SetBody(handle, state);
            }
        }

        public float Mass => world.GetMass(handle);

        public bool IsKinematic => world.GetKind(handle) == BodyKind.Kinematic;

        public BodyState2D State
        {
            get => world.GetBody(handle);
            set => world.SetBody(handle, value);
        }

        public void AddForce(Vector2 force) => world.AddForce(handle, force);

        public void AddImpulse(Vector2 impulse) => world.AddImpulse(handle, impulse);
    }
}
