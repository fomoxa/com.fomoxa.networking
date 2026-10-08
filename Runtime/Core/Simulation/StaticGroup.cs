using System;

namespace Fomoxa.Networking.Simulation
{
    public readonly struct StaticGroup : IEquatable<StaticGroup>
    {
        public StaticGroup(uint sceneId, int id, int firstIndex, int count)
        {
            SceneId = sceneId;
            Id = id;
            FirstIndex = firstIndex;
            Count = count;
        }

        public uint SceneId { get; }

        public int Id { get; }

        public int FirstIndex { get; }

        public int Count { get; }

        public bool IsValid => Id != 0;

        public bool Equals(StaticGroup other) => SceneId == other.SceneId && Id == other.Id && FirstIndex == other.FirstIndex && Count == other.Count;

        public override bool Equals(object obj) => obj is StaticGroup other && Equals(other);

        public override int GetHashCode() => ((int)SceneId * 397) ^ Id;
    }
}
