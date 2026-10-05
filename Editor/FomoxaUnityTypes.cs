using System;
using System.Collections.Generic;

namespace Fomoxa.Unity.Editor
{
    public readonly struct FomoxaUnityField
    {
        public FomoxaUnityField(string name, string wireType)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            WireType = FomoxaUnityTypes.IsScalar(wireType)
                ? wireType
                : throw new ArgumentException($"'{wireType}' is not a scalar wire type; a Unity type field is bool, i8 – u64, f32 or f64", nameof(wireType));
        }

        public string Name { get; }

        public string WireType { get; }
    }

    public readonly struct FomoxaUnityType
    {
        public FomoxaUnityType(string fullTypeName, IReadOnlyList<FomoxaUnityField> fields)
        {
            FullTypeName = fullTypeName;
            Fields = fields;
        }

        public string FullTypeName { get; }

        public IReadOnlyList<FomoxaUnityField> Fields { get; }
    }

    public static class FomoxaUnityTypes
    {
        private static readonly HashSet<string> Scalars = new HashSet<string>(StringComparer.Ordinal)
        {
            "bool", "i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64", "f32", "f64",
        };

        private static readonly Dictionary<string, FomoxaUnityType> Types = new Dictionary<string, FomoxaUnityType>(StringComparer.Ordinal)
        {
            ["Vector2"] = Layout("UnityEngine.Vector2", ("x", "f32"), ("y", "f32")),
            ["Vector3"] = Layout("UnityEngine.Vector3", ("x", "f32"), ("y", "f32"), ("z", "f32")),
            ["Vector4"] = Layout("UnityEngine.Vector4", ("x", "f32"), ("y", "f32"), ("z", "f32"), ("w", "f32")),
            ["Vector2Int"] = Layout("UnityEngine.Vector2Int", ("x", "i32"), ("y", "i32")),
            ["Vector3Int"] = Layout("UnityEngine.Vector3Int", ("x", "i32"), ("y", "i32"), ("z", "i32")),
            ["Quaternion"] = Layout("UnityEngine.Quaternion", ("x", "f32"), ("y", "f32"), ("z", "f32"), ("w", "f32")),
            ["Color"] = Layout("UnityEngine.Color", ("r", "f32"), ("g", "f32"), ("b", "f32"), ("a", "f32")),
            ["Color32"] = Layout("UnityEngine.Color32", ("r", "u8"), ("g", "u8"), ("b", "u8"), ("a", "u8")),
            ["Rect"] = Layout("UnityEngine.Rect", ("x", "f32"), ("y", "f32"), ("width", "f32"), ("height", "f32")),
        };

        public static void Register(string wireTypeName, string fullTypeName, params FomoxaUnityField[] fields)
        {
            if (string.IsNullOrEmpty(wireTypeName) || string.IsNullOrEmpty(fullTypeName) || fields == null || fields.Length == 0)
            {
                throw new ArgumentException("a Unity type needs a wire type name, a full type name and at least one field");
            }

            Types[wireTypeName] = new FomoxaUnityType(fullTypeName, (FomoxaUnityField[])fields.Clone());
        }

        public static bool TryGet(string wireTypeName, out FomoxaUnityType type) =>
            Types.TryGetValue(wireTypeName, out type);

        internal static bool IsScalar(string wireType) => wireType != null && Scalars.Contains(wireType);

        private static FomoxaUnityType Layout(string fullTypeName, params (string Name, string WireType)[] fields)
        {
            var layout = new FomoxaUnityField[fields.Length];
            for (int index = 0; index < fields.Length; index++)
            {
                layout[index] = new FomoxaUnityField(fields[index].Name, fields[index].WireType);
            }

            return new FomoxaUnityType(fullTypeName, layout);
        }
    }
}
