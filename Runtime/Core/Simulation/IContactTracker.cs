namespace Fomoxa.Networking.Simulation
{
    public interface IContactTracker
    {
        void Query();

        void Record(uint tick, int capacity);

        void Restore(uint tick);

        void Publish();
    }
}
