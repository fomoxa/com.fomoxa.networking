using System;
using System.Runtime.InteropServices;

namespace Fomoxa.Unity
{
    internal sealed class JsBrowserSocket : IBrowserSocket
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public int Open(string url) => FomoxaWsOpen(url);

        public BrowserSocketState State(int id) => (BrowserSocketState)FomoxaWsState(id);

        public int BufferedAmount(int id) => FomoxaWsBufferedAmount(id);

        public void Send(int id, byte[] data, int length) => FomoxaWsSend(id, data, length);

        public int NextLength(int id) => FomoxaWsNextLength(id);

        public int Receive(int id, byte[] buffer, int capacity) => FomoxaWsReceive(id, buffer, capacity);

        public void Close(int id) => FomoxaWsClose(id);

        public void Release(int id) => FomoxaWsRelease(id);

        [DllImport("__Internal")]
        private static extern int FomoxaWsOpen(string url);

        [DllImport("__Internal")]
        private static extern int FomoxaWsState(int id);

        [DllImport("__Internal")]
        private static extern int FomoxaWsBufferedAmount(int id);

        [DllImport("__Internal")]
        private static extern void FomoxaWsSend(int id, byte[] data, int length);

        [DllImport("__Internal")]
        private static extern int FomoxaWsNextLength(int id);

        [DllImport("__Internal")]
        private static extern int FomoxaWsReceive(int id, byte[] buffer, int capacity);

        [DllImport("__Internal")]
        private static extern void FomoxaWsClose(int id);

        [DllImport("__Internal")]
        private static extern void FomoxaWsRelease(int id);
#else
        public int Open(string url) => throw NotInBrowser();

        public BrowserSocketState State(int id) => throw NotInBrowser();

        public int BufferedAmount(int id) => throw NotInBrowser();

        public void Send(int id, byte[] data, int length) => throw NotInBrowser();

        public int NextLength(int id) => throw NotInBrowser();

        public int Receive(int id, byte[] buffer, int capacity) => throw NotInBrowser();

        public void Close(int id) => throw NotInBrowser();

        public void Release(int id) => throw NotInBrowser();

        private static PlatformNotSupportedException NotInBrowser() =>
            new PlatformNotSupportedException("the browser WebSocket bridge only runs in a WebGL player");
#endif
    }
}
