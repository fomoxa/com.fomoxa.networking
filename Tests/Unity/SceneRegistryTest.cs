using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests
{
    public sealed class SceneRegistryTest
    {
        [Test]
        public void RegisterRefusesIdZeroATakenIdAndNoLoader()
        {
            var registry = new SceneRegistry();
            registry.Register(0x11, new NoLoader());

            Assert.Throws<ArgumentException>(() => registry.Register(0, new NoLoader()));
            Assert.Throws<ArgumentException>(() => registry.Register(0x11, new NoLoader()));
            Assert.Throws<ArgumentNullException>(() => registry.Register(0x12, null));
            Assert.IsTrue(registry.Contains(0x11));
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void BuildScenesAreRegisteredAndFoundByPath()
        {
            var list = ScriptableObject.CreateInstance<NetworkSceneList>();
            list.Set(new[] { (0x21u, "Assets/Scenes/Arena.unity"), (0x22u, "Assets/Scenes/Lobby.unity") });
            var registry = new SceneRegistry();

            registry.RegisterBuildScenes(list);

            Assert.AreEqual(0x21u, registry.IdOf("Assets/Scenes/Arena.unity"));
            Assert.AreEqual(0x22u, registry.IdOf("Assets/Scenes/Lobby.unity"));
            Assert.AreEqual(0u, registry.IdOf("Assets/Scenes/Missing.unity"));
            Assert.AreEqual(0u, registry.IdOf(null));
            Assert.IsTrue(registry.Contains(0x22));
            UnityEngine.Object.DestroyImmediate(list);
        }

        [Test]
        public void TheBuildLoaderReportsAFailureWhenUnityRefusesToLoad()
        {
            var list = ScriptableObject.CreateInstance<NetworkSceneList>();
            list.Set(new[] { (0x31u, "Assets/Scenes/NotInTheBuild.unity") });
            var registry = new SceneRegistry();
            registry.RegisterBuildScenes(list);
            Assert.IsTrue(registry.TryGet(0x31, out ISceneLoader loader));
            Exception failure = null;
            bool loaded = false;

            loader.Load(scene => loaded = true, exception => failure = exception);

            Assert.IsFalse(loaded);
            Assert.IsNotNull(failure);
            UnityEngine.Object.DestroyImmediate(list);
        }

        private sealed class NoLoader : ISceneLoader
        {
            public void Load(Action<Scene> loaded, Action<Exception> failed)
            {
            }

            public void Unload(Scene scene, Action unloaded)
            {
            }
        }
    }
}
