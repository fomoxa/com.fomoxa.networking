using UnityEngine;

namespace Fomoxa.Unity
{
    public readonly struct NetworkContactPoint
    {
        public NetworkContactPoint(Vector3 point, Vector3 normal, float depth)
        {
            Point = point;
            Normal = normal;
            Depth = depth;
        }

        public Vector3 Point { get; }

        public Vector3 Normal { get; }

        public float Depth { get; }
    }

    public readonly struct NetworkContactPoint2D
    {
        public NetworkContactPoint2D(Vector2 point, Vector2 normal, float depth)
        {
            Point = point;
            Normal = normal;
            Depth = depth;
        }

        public Vector2 Point { get; }

        public Vector2 Normal { get; }

        public float Depth { get; }
    }
}
