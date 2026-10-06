using System.Collections.Generic;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Unity.Tests
{
    internal static class PhysicsSteps
    {
        public static void Step(PhysicsWorlds worlds, float seconds)
        {
            var stepping = new List<IPhysicsSimulation>();
            worlds.WorldsToStep(stepping);
            foreach (IPhysicsSimulation world in stepping)
            {
                world.Step(seconds);
            }
        }

        public static void StepAndPublish(PhysicsWorlds worlds, float seconds, uint tick, bool record, int capacity)
        {
            var stepping = new List<IPhysicsSimulation>();
            worlds.WorldsToStep(stepping);
            foreach (IPhysicsSimulation world in stepping)
            {
                world.Step(seconds);
            }

            var queried = new List<IContactTracker>();
            foreach (IPhysicsSimulation world in stepping)
            {
                IContactTracker tracker = worlds.TrackerOf(world);
                if (tracker == null)
                {
                    continue;
                }

                queried.Add(tracker);
                tracker.Query();
                if (record)
                {
                    tracker.Record(tick, capacity);
                }
            }

            foreach (IContactTracker tracker in queried)
            {
                tracker.Publish();
            }
        }
    }
}
