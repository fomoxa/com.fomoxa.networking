using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;

namespace Fomoxa.Unity
{
    internal static class NetworkPlayerLoop
    {
        public static void InsertFirst(Type phase, PlayerLoopSystem.UpdateFunction update) => Insert(phase, update, false);

        public static void InsertLast(Type phase, PlayerLoopSystem.UpdateFunction update) => Insert(phase, update, true);

        public static void Remove(PlayerLoopSystem.UpdateFunction update)
        {
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] phases = root.subSystemList;
            for (int index = 0; index < phases.Length; index++)
            {
                PlayerLoopSystem[] systems = phases[index].subSystemList;
                if (systems == null)
                {
                    continue;
                }

                phases[index].subSystemList = Array.FindAll(systems, system => system.updateDelegate != update);
            }

            PlayerLoop.SetPlayerLoop(root);
        }

        private static void Insert(Type phase, PlayerLoopSystem.UpdateFunction update, bool last)
        {
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] phases = root.subSystemList;
            for (int index = 0; index < phases.Length; index++)
            {
                if (phases[index].type != phase)
                {
                    continue;
                }

                var systems = new List<PlayerLoopSystem>(phases[index].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                var system = new PlayerLoopSystem { type = typeof(NetworkManager), updateDelegate = update };
                if (last)
                {
                    systems.Add(system);
                }
                else
                {
                    systems.Insert(0, system);
                }

                phases[index].subSystemList = systems.ToArray();
                PlayerLoop.SetPlayerLoop(root);
                return;
            }

            throw new InvalidOperationException($"the player loop has no {phase.Name} phase");
        }
    }
}
