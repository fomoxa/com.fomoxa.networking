using System.Numerics;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Standalone
{
    public readonly struct ChildBodyDesc
    {
        public ChildBodyDesc(in BodyDesc body, Vector3 localPosition, Quaternion localRotation)
        {
            Body = body;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }

        public BodyDesc Body { get; }

        public Vector3 LocalPosition { get; }

        public Quaternion LocalRotation { get; }
    }

    public readonly struct ChildBodyDesc2D
    {
        public ChildBodyDesc2D(in BodyDesc2D body, Vector2 localPosition, float localRotation)
        {
            Body = body;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }

        public BodyDesc2D Body { get; }

        public Vector2 LocalPosition { get; }

        public float LocalRotation { get; }
    }
}
