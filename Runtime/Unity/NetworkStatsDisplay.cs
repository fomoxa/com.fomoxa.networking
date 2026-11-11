using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;
using UnityEngine;

namespace Fomoxa.Unity
{
    public enum ScreenCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    public sealed class NetworkStatsDisplay : MonoBehaviour
    {
        private const float MinAverageSeconds = 0.1f;
        private const float Margin = 8f;
        private const double BytesPerKibibyte = 1024;

        [SerializeField] private NetworkManager manager;
        [SerializeField] private bool showConnection = true;
        [SerializeField] private bool showTraffic;
        [SerializeField] private bool showTime = true;
        [SerializeField] private bool showPrediction;
        [SerializeField] private ScreenCorner corner = ScreenCorner.TopLeft;
        [SerializeField, Min(MinAverageSeconds)] private float averageSeconds = 1f;
        [SerializeField] private bool runInReleaseBuilds;

        private readonly StringBuilder text = new StringBuilder();
        private readonly GUIContent content = new GUIContent();
        private GUIStyle style;
        private float sampledAt = -1f;
        private TrafficSample clientSent;
        private TrafficSample clientReceived;
        private TrafficSample serverSent;
        private TrafficSample serverReceived;
        private long reconciles;

        internal string Text => content.text;

        private bool Shown => runInReleaseBuilds || Debug.isDebugBuild;

        private float AverageSeconds => Mathf.Max(MinAverageSeconds, averageSeconds);

        private NetworkManager Manager => manager != null ? manager : GetComponent<NetworkManager>();

        private void Update()
        {
            if (!Shown)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (sampledAt < 0f || now - sampledAt >= AverageSeconds)
            {
                Refresh(now);
            }
        }

        private void OnGUI()
        {
            if (!Shown || content.text.Length == 0)
            {
                return;
            }

            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = false };
            GUI.Box(RectAt(style.CalcSize(content)), content, style);
        }

        internal void Refresh(float now)
        {
            float elapsed = sampledAt < 0f ? 0f : now - sampledAt;
            sampledAt = now;
            text.Clear();
            NetworkManager target = Manager;
            if (target == null)
            {
                text.Append("NetworkStatsDisplay has no NetworkManager");
            }
            else
            {
                Describe(target, elapsed);
            }

            content.text = text.ToString().TrimEnd('\n');
        }

        private void Describe(NetworkManager target, float elapsed)
        {
            ClientManager client = target.ClientManager;
            ServerManager server = target.ServerManager;
            bool clientRunning = client != null && client.State != ConnectionState.Stopped;
            bool serverRunning = server != null && server.State != ServerState.Stopped;
            if (showConnection && !clientRunning && !serverRunning)
            {
                text.Append("Client and server stopped\n");
            }

            if (clientRunning)
            {
                DescribeClient(client, elapsed);
            }

            if (serverRunning)
            {
                DescribeServer(server, elapsed);
            }

            TimeManager time = target.TimeManager;
            if (showTime && time != null)
            {
                text.Append("Tick ").Append(time.Tick)
                    .Append(", server ").Append(time.ServerTick)
                    .Append(", prediction ").Append(time.PredictionTick)
                    .Append(time.ClockSynced ? ", clock synced\n" : ", clock not synced\n");
            }
        }

        private void DescribeClient(ClientManager client, float elapsed)
        {
            if (showConnection)
            {
                text.Append("Client ").Append(client.State)
                    .Append(", RTT ").Append(client.Rtt.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)).Append(" ms\n");
            }

            if (showTraffic)
            {
                TrafficStatistics statistics = client.Statistics;
                if (statistics == null)
                {
                    text.Append("Client traffic: turn on Collect Statistics on the NetworkManager\n");
                }
                else
                {
                    DescribeDirection("Client sent ", "resent", statistics.Sent, ref clientSent, elapsed);
                    DescribeDirection("Client received ", "duplicates", statistics.Received, ref clientReceived, elapsed);
                }
            }

            if (showPrediction)
            {
                long total = ReconcilesOf(client.Spawned);
                double rate = RateOf(total - reconciles, elapsed);
                reconciles = total;
                text.Append("Reconciles ").Append(total).Append(", ").Append(rate.ToString("0.0", CultureInfo.InvariantCulture)).Append("/s\n");
            }
        }

        private void DescribeServer(ServerManager server, float elapsed)
        {
            if (showConnection)
            {
                text.Append("Server ").Append(server.State).Append(", ").Append(server.PeerCount).Append(" peers\n");
            }

            if (showTraffic)
            {
                ServerTrafficStatistics statistics = server.Statistics;
                if (statistics == null)
                {
                    text.Append("Server traffic: turn on Collect Statistics on the NetworkManager\n");
                }
                else
                {
                    DescribeDirection("Server sent ", "resent", statistics.Sent, ref serverSent, elapsed);
                    DescribeDirection("Server received ", "duplicates", statistics.Received, ref serverReceived, elapsed);
                }
            }

            if (showPrediction)
            {
                text.Append("Dropped inputs ").Append(server.DroppedInputs).Append('\n');
            }
        }

        private void DescribeDirection(string label, string repeatedLabel, TrafficDirection direction, ref TrafficSample previous, float elapsed)
        {
            var current = new TrafficSample(direction);
            text.Append(label)
                .Append(RateOf(current.Bundles - previous.Bundles, elapsed).ToString("0", CultureInfo.InvariantCulture)).Append(" bundles/s, ")
                .Append((RateOf(current.Bytes - previous.Bytes, elapsed) / BytesPerKibibyte).ToString("0.0", CultureInfo.InvariantCulture)).Append(" KiB/s, ")
                .Append(RateOf(current.Messages - previous.Messages, elapsed).ToString("0", CultureInfo.InvariantCulture)).Append(" messages/s, ")
                .Append(RateOf(current.Repeated - previous.Repeated, elapsed).ToString("0", CultureInfo.InvariantCulture)).Append(' ').Append(repeatedLabel).Append("/s\n");
            previous = current;
        }

        private static double RateOf(long delta, float elapsed) => elapsed > 0f && delta > 0 ? delta / (double)elapsed : 0d;

        private static long ReconcilesOf(IReadOnlyDictionary<uint, INetworkEntity> spawned)
        {
            long total = 0;
            foreach (INetworkEntity entity in spawned.Values)
            {
                foreach (EntityBehaviour behaviour in entity.EntityBehaviours)
                {
                    total += behaviour.ReconcileCount;
                }
            }

            return total;
        }

        private Rect RectAt(Vector2 size)
        {
            bool left = corner == ScreenCorner.TopLeft || corner == ScreenCorner.BottomLeft;
            bool top = corner == ScreenCorner.TopLeft || corner == ScreenCorner.TopRight;
            float x = left ? Margin : Screen.width - size.x - Margin;
            float y = top ? Margin : Screen.height - size.y - Margin;
            return new Rect(x, y, size.x, size.y);
        }

        private readonly struct TrafficSample
        {
            public TrafficSample(TrafficDirection direction)
            {
                Bundles = direction.Bundles.Count;
                Bytes = direction.Bundles.Bytes;
                Messages = direction.Total.New.Count;
                Repeated = direction.Total.Repeated.Count;
            }

            public long Bundles { get; }

            public long Bytes { get; }

            public long Messages { get; }

            public long Repeated { get; }
        }
    }
}
