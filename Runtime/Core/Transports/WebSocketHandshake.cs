using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Fomoxa.Networking.Transports
{
    internal static class WebSocketHandshake
    {
        private const string AcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        public static string NewKey()
        {
            var nonce = new byte[16];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }

            return Convert.ToBase64String(nonce);
        }

        public static string AcceptFor(string key)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                return Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + AcceptGuid)));
            }
        }

        public static byte[] Request(string target, string host, string key) =>
            Encoding.ASCII.GetBytes(
                $"GET {target} HTTP/1.1\r\nHost: {host}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n");

        public static int HeaderEnd(byte[] buffer, int used)
        {
            for (int index = 3; index < used; index++)
            {
                if (buffer[index] == '\n' && buffer[index - 1] == '\r' && buffer[index - 2] == '\n' && buffer[index - 3] == '\r')
                {
                    return index + 1;
                }
            }

            return -1;
        }

        public static bool AcceptsResponse(byte[] buffer, int headerEnd, string key)
        {
            if (!TryParse(buffer, headerEnd, out string startLine, out Dictionary<string, string> headers))
            {
                return false;
            }

            string[] parts = startLine.Split(' ');
            return parts.Length >= 2
                && parts[0] == "HTTP/1.1"
                && parts[1] == "101"
                && HeaderIs(headers, "upgrade", "websocket")
                && HeaderHasToken(headers, "connection", "upgrade")
                && headers.TryGetValue("sec-websocket-accept", out string accept)
                && accept == AcceptFor(key)
                && !headers.ContainsKey("sec-websocket-extensions")
                && !headers.ContainsKey("sec-websocket-protocol");
        }

        public static byte[] Respond(byte[] buffer, int headerEnd, WebSocketSettings settings, out bool accepted)
        {
            accepted = false;
            if (!TryParse(buffer, headerEnd, out string startLine, out Dictionary<string, string> headers))
            {
                return Status("400 Bad Request");
            }

            string[] parts = startLine.Split(' ');
            if (parts.Length != 3 || parts[0] != "GET" || parts[2] != "HTTP/1.1" || !headers.ContainsKey("host"))
            {
                return Status("400 Bad Request");
            }

            int query = parts[1].IndexOf('?');
            string path = query < 0 ? parts[1] : parts[1].Substring(0, query);
            if (path != settings.Path)
            {
                return Status("404 Not Found");
            }

            if (settings.AllowedOrigins.Count > 0
                && headers.TryGetValue("origin", out string origin)
                && !settings.AllowedOrigins.Contains(origin))
            {
                return Status("403 Forbidden");
            }

            if (!HeaderIs(headers, "upgrade", "websocket")
                || !HeaderHasToken(headers, "connection", "upgrade")
                || !headers.TryGetValue("sec-websocket-key", out string key)
                || !IsNonce(key))
            {
                return Status("400 Bad Request");
            }

            if (!HeaderIs(headers, "sec-websocket-version", "13"))
            {
                return Encoding.ASCII.GetBytes("HTTP/1.1 426 Upgrade Required\r\nSec-WebSocket-Version: 13\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            }

            accepted = true;
            return Encoding.ASCII.GetBytes(
                $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {AcceptFor(key)}\r\n\r\n");
        }

        public static byte[] Status(string status) =>
            Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        private static bool IsNonce(string key)
        {
            try
            {
                return Convert.FromBase64String(key).Length == 16;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool HeaderIs(Dictionary<string, string> headers, string name, string value) =>
            headers.TryGetValue(name, out string actual) && string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);

        private static bool HeaderHasToken(Dictionary<string, string> headers, string name, string token)
        {
            if (!headers.TryGetValue(name, out string actual))
            {
                return false;
            }

            foreach (string part in actual.Split(','))
            {
                if (string.Equals(part.Trim(), token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParse(byte[] buffer, int headerEnd, out string startLine, out Dictionary<string, string> headers)
        {
            headers = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] lines = Encoding.ASCII.GetString(buffer, 0, headerEnd - 4).Split(new[] { "\r\n" }, StringSplitOptions.None);
            startLine = lines[0];
            for (int index = 1; index < lines.Length; index++)
            {
                int colon = lines[index].IndexOf(':');
                if (colon <= 0)
                {
                    return false;
                }

                string name = lines[index].Substring(0, colon).Trim().ToLowerInvariant();
                string value = lines[index].Substring(colon + 1).Trim();
                headers[name] = headers.TryGetValue(name, out string earlier) ? earlier + ", " + value : value;
            }

            return true;
        }
    }
}
