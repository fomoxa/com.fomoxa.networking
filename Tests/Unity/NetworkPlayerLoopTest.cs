using System;
using NUnit.Framework;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Fomoxa.Unity.Tests
{
    public sealed class NetworkPlayerLoopTest
    {
        private static PlayerLoopSystem[] SystemsOf(Type phase)
        {
            foreach (PlayerLoopSystem system in PlayerLoop.GetCurrentPlayerLoop().subSystemList)
            {
                if (system.type == phase)
                {
                    return system.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                }
            }

            return Array.Empty<PlayerLoopSystem>();
        }

        private static void Noop()
        {
        }

        [Test]
        public void InsertFirstPutsTheSystemAtTheStartOfThePhaseAndRemoveTakesItOut()
        {
            PlayerLoopSystem.UpdateFunction update = Noop;
            NetworkPlayerLoop.InsertFirst(typeof(EarlyUpdate), update);
            try
            {
                Assert.AreEqual(update, SystemsOf(typeof(EarlyUpdate))[0].updateDelegate);
            }
            finally
            {
                NetworkPlayerLoop.Remove(update);
            }

            Assert.IsFalse(Array.Exists(SystemsOf(typeof(EarlyUpdate)), system => system.updateDelegate == update));
        }

        [Test]
        public void InsertLastPutsTheSystemAtTheEndOfThePhase()
        {
            PlayerLoopSystem.UpdateFunction update = Noop;
            NetworkPlayerLoop.InsertLast(typeof(PostLateUpdate), update);
            try
            {
                PlayerLoopSystem[] systems = SystemsOf(typeof(PostLateUpdate));
                Assert.AreEqual(update, systems[systems.Length - 1].updateDelegate);
            }
            finally
            {
                NetworkPlayerLoop.Remove(update);
            }
        }
    }
}
