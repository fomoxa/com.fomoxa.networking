using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Objects
{
    public readonly struct SpawnData
    {
        public SpawnData(uint prefabFingerprint, Vector3 position, Quaternion rotation, Vector3 scale, IReadOnlyList<ReadOnlyMemory<byte>> states = null)
        {
            PrefabFingerprint = prefabFingerprint;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            States = states ?? Array.Empty<ReadOnlyMemory<byte>>();
        }

        public uint PrefabFingerprint { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 Scale { get; }

        public IReadOnlyList<ReadOnlyMemory<byte>> States { get; }
    }

    public delegate SpawnData SpawnSource(uint objectId);

    public static class SpawnTransform
    {
        public const byte PositionBit = 1;
        public const byte RotationBit = 2;
        public const byte ScaleBit = 4;
        public const byte AllBits = PositionBit | RotationBit | ScaleBit;

        public static void Pack(in SpawnData data, ObjectSpawn spawn)
        {
            spawn.Mask = Pack(data, spawn.Values);
            PackStates(data.States, spawn.States);
        }

        public static void Pack(in SpawnData data, ObjectSceneSpawn spawn)
        {
            spawn.Mask = Pack(data, spawn.Values);
            PackStates(data.States, spawn.States);
        }

        public static void PackStates(IReadOnlyList<ReadOnlyMemory<byte>> states, List<ReadOnlyMemory<byte>> target)
        {
            target.Clear();
            for (int index = 0; index < states.Count; index++)
            {
                target.Add(states[index]);
            }
        }

        public static void PackSelected(byte mask, Vector3 position, Quaternion rotation, Vector3 scale, List<float> values)
        {
            if ((mask & ~AllBits) != 0)
            {
                throw new ArgumentException($"mask 0x{mask:X2} has bits outside position, rotation and scale", nameof(mask));
            }

            values.Clear();
            if ((mask & PositionBit) != 0)
            {
                values.Add(position.X);
                values.Add(position.Y);
                values.Add(position.Z);
            }

            if ((mask & RotationBit) != 0)
            {
                values.Add(rotation.X);
                values.Add(rotation.Y);
                values.Add(rotation.Z);
                values.Add(rotation.W);
            }

            if ((mask & ScaleBit) != 0)
            {
                values.Add(scale.X);
                values.Add(scale.Y);
                values.Add(scale.Z);
            }
        }

        public static byte Pack(in SpawnData data, List<float> values)
        {
            values.Clear();
            byte mask = 0;
            if (data.Position != Vector3.Zero)
            {
                mask |= PositionBit;
                values.Add(data.Position.X);
                values.Add(data.Position.Y);
                values.Add(data.Position.Z);
            }

            if (data.Rotation != Quaternion.Identity)
            {
                mask |= RotationBit;
                values.Add(data.Rotation.X);
                values.Add(data.Rotation.Y);
                values.Add(data.Rotation.Z);
                values.Add(data.Rotation.W);
            }

            if (data.Scale != Vector3.One)
            {
                mask |= ScaleBit;
                values.Add(data.Scale.X);
                values.Add(data.Scale.Y);
                values.Add(data.Scale.Z);
            }

            return mask;
        }

        public static bool TryUnpack(byte mask, List<float> values, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = Vector3.Zero;
            rotation = Quaternion.Identity;
            scale = Vector3.One;
            if ((mask & ~AllBits) != 0 || values.Count != ValueCount(mask))
            {
                return false;
            }

            int next = 0;
            if ((mask & PositionBit) != 0)
            {
                position = new Vector3(values[next], values[next + 1], values[next + 2]);
                next += 3;
            }

            if ((mask & RotationBit) != 0)
            {
                rotation = new Quaternion(values[next], values[next + 1], values[next + 2], values[next + 3]);
                next += 4;
            }

            if ((mask & ScaleBit) != 0)
            {
                scale = new Vector3(values[next], values[next + 1], values[next + 2]);
            }

            return true;
        }

        private static int ValueCount(byte mask) =>
            ((mask & PositionBit) != 0 ? 3 : 0) + ((mask & RotationBit) != 0 ? 4 : 0) + ((mask & ScaleBit) != 0 ? 3 : 0);
    }
}
