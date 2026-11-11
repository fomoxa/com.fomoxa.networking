using System;
using Fomoxa.Net;
using Fomoxa.Networking.Transports;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class SimulatedNetworkTransport : NetworkTransport
    {
        [SerializeField] private NetworkTransport transport;
        [SerializeField, Min(0f)] private float latencyMilliseconds;
        [SerializeField, Min(0f)] private float jitterMilliseconds;
        [SerializeField, Range(0f, 100f)] private float packetLoss;
        [SerializeField, Range(0f, 100f)] private float duplicate;
        [SerializeField, Range(0f, 100f)] private float reorder;
        [SerializeField, Min(1)] private int maxHeldPackets = 1024;

        private readonly NetworkConditions conditions = new NetworkConditions();

        internal static bool? DebugBuildOverride { get; set; }

        internal NetworkConditions Conditions => conditions;

        private static bool IsDebugBuild => DebugBuildOverride ?? Debug.isDebugBuild;

        public override ITransportFactory CreateFactory()
        {
            if (transport == null)
            {
                throw new InvalidOperationException($"{name} has no transport to simulate");
            }

            if (transport == this)
            {
                throw new InvalidOperationException($"{name} cannot simulate itself");
            }

            ITransportFactory real = transport.CreateFactory()
                ?? throw new InvalidOperationException($"{transport.GetType().Name} returned no transport factory");
            if (!IsDebugBuild)
            {
                Debug.LogWarning($"{name}: network simulation runs only in the Editor and in Development builds; {transport.GetType().Name} is used directly");
                return real;
            }

            Apply();
            return new SimulatedTransportFactory(real, conditions, () => MonotonicClock.Now);
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Apply()
        {
            conditions.Latency = TimeSpan.FromMilliseconds(Mathf.Max(0f, latencyMilliseconds));
            conditions.Jitter = TimeSpan.FromMilliseconds(Mathf.Max(0f, jitterMilliseconds));
            conditions.PacketLoss = Mathf.Clamp(packetLoss, 0f, 100f);
            conditions.Duplicate = Mathf.Clamp(duplicate, 0f, 100f);
            conditions.Reorder = Mathf.Clamp(reorder, 0f, 100f);
            conditions.MaxHeldPackets = Mathf.Max(1, maxHeldPackets);
        }
    }
}
