using System;
using Fomoxa.Networking.Transports;
using UnityEngine;

namespace Fomoxa.Unity
{
    public abstract class NetworkTransport : MonoBehaviour
    {
        internal static bool? BrowserOverride { get; set; }

        internal static bool RunsInBrowser => BrowserOverride ?? Application.platform == RuntimePlatform.WebGLPlayer;

        public abstract ITransportFactory CreateFactory();

        internal static PlatformNotSupportedException NotInBrowser(string transportName) =>
            new PlatformNotSupportedException($"{transportName} cannot run in a browser; assign a WebSocketNetworkTransport or a MultiNetworkTransport to the NetworkManager for WebGL builds");
    }
}
