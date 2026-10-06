using System.Collections.Generic;

namespace Fomoxa.Networking.Simulation
{
    public sealed class ContactTracker<TContact> : IContactTracker
    {
        private readonly List<IContactSource<TContact>> sources = new List<IContactSource<TContact>>();
        private readonly List<IContactSource<TContact>> visiting = new List<IContactSource<TContact>>();

        public int Count => sources.Count;

        public void Add(IContactSource<TContact> source)
        {
            if (!sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        public void Remove(IContactSource<TContact> source) => sources.Remove(source);

        public void Query()
        {
            visiting.Clear();
            visiting.AddRange(sources);
            foreach (IContactSource<TContact> source in visiting)
            {
                if (source.MovedWorld)
                {
                    source.Rejoin();
                }
                else
                {
                    source.Contacts.Query(source);
                }
            }

            visiting.Clear();
        }

        public void Record(uint tick, int capacity)
        {
            foreach (IContactSource<TContact> source in sources)
            {
                source.Contacts.Record(tick, capacity);
            }
        }

        public void Restore(uint tick)
        {
            foreach (IContactSource<TContact> source in sources)
            {
                source.Contacts.Restore(tick);
            }
        }

        public void Publish()
        {
            visiting.Clear();
            visiting.AddRange(sources);
            foreach (IContactSource<TContact> source in visiting)
            {
                source.Contacts.Publish(source);
            }

            visiting.Clear();
        }
    }
}
