namespace Fomoxa.Networking.Standalone
{
    public readonly struct BehaviourSite
    {
        private readonly ChildBody body;
        private readonly bool hasBody;

        public BehaviourSite(ChildBody body)
        {
            this.body = body;
            hasBody = true;
        }

        public bool TryGetBody(out ChildBody body)
        {
            body = this.body;
            return hasBody;
        }
    }
}
