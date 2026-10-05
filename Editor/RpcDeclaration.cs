using Fomoxa.Networking;

namespace Fomoxa.Unity.Editor
{
    public readonly struct RpcDeclaration
    {
        public RpcDeclaration(string type, string method, string model, Channel channel)
        {
            Type = type;
            Method = method;
            Model = model;
            Channel = channel;
        }

        public string Type { get; }

        public string Method { get; }

        public string Model { get; }

        public Channel Channel { get; }
    }
}
