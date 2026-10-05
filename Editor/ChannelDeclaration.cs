using Fomoxa.Networking;

namespace Fomoxa.Unity.Editor
{
    public readonly struct ChannelDeclaration
    {
        public ChannelDeclaration(string model, string codec, Channel channel)
        {
            Model = model;
            Codec = codec;
            Channel = channel;
        }

        public string Model { get; }

        public string Codec { get; }

        public Channel Channel { get; }
    }
}
