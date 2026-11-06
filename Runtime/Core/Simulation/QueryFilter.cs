namespace Fomoxa.Networking.Simulation
{
    public readonly struct QueryFilter
    {
        private readonly int skippedLayers;
        private readonly bool skipsTriggers;

        public QueryFilter(int layerMask, BodyHandle excludeBody, bool includeTriggers)
        {
            skippedLayers = ~layerMask;
            ExcludeBody = excludeBody;
            skipsTriggers = !includeTriggers;
        }

        public static QueryFilter All => default;

        public int LayerMask => ~skippedLayers;

        public BodyHandle ExcludeBody { get; }

        public bool IncludeTriggers => !skipsTriggers;
    }
}
