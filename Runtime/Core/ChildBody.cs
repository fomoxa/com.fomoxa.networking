using System;

namespace Fomoxa.Networking
{
    public readonly struct ChildBody : IEquatable<ChildBody>
    {
        private ChildBody(int index, bool is2D)
        {
            Index = index;
            Is2D = is2D;
        }

        public int Index { get; }

        public bool Is2D { get; }

        public static ChildBody Of(int index) => new ChildBody(CheckIndex(index), false);

        public static ChildBody Of2D(int index) => new ChildBody(CheckIndex(index), true);

        public bool Equals(ChildBody other) => Index == other.Index && Is2D == other.Is2D;

        public override bool Equals(object obj) => obj is ChildBody other && Equals(other);

        public override int GetHashCode() => (Index * 2) + (Is2D ? 1 : 0);

        public override string ToString() => Is2D ? $"ChildBody.Of2D({Index})" : $"ChildBody.Of({Index})";

        private static int CheckIndex(int index) => index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(index), index, "a child body index is not negative");
    }
}
