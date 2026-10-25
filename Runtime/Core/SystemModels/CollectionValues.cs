using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class BoolValue
    {
        [Network("bool")]
        [Codec("net")]
        public bool Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class I8Value
    {
        [Network("i8")]
        [Codec("net")]
        public sbyte Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class U8Value
    {
        [Network("u8")]
        [Codec("net")]
        public byte Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class I16Value
    {
        [Network("i16")]
        [Codec("net")]
        public short Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class U16Value
    {
        [Network("u16")]
        [Codec("net")]
        public ushort Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class I32Value
    {
        [Network("i32")]
        [Codec("net")]
        public int Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class U32Value
    {
        [Network("u32")]
        [Codec("net")]
        public uint Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class I64Value
    {
        [Network("i64")]
        [Codec("net")]
        public long Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class U64Value
    {
        [Network("u64")]
        [Codec("net")]
        public ulong Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class F32Value
    {
        [Network("f32")]
        [Codec("net")]
        public float Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class F64Value
    {
        [Network("f64")]
        [Codec("net")]
        public double Value { get; set; }
    }

    [Network]
    [Codec("net")]
    public class StringValue
    {
        [Network("string")]
        [Codec("net")]
        public string Value { get; set; } = string.Empty;
    }
}
