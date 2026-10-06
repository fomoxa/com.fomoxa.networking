using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests
{
    public sealed class UnityServerSceneHostTest
    {
        private const uint SceneId = 0x51;

        private readonly List<GameObject> created = new List<GameObject>();
        private Scene scene;

        [SetUp]
        public void CreateScene()
        {
            scene = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void DestroyScene()
        {
            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            created.Clear();
            EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void TheSceneObjectsOfASceneBeingUnloadedAreNotPresented()
        {
            var loader = new HeldLoader(scene);
            var registry = new SceneRegistry();
            registry.Register(SceneId, loader);
            NetworkObject inScene = Create("InScene");
            SceneManager.MoveGameObjectToScene(inScene.gameObject, scene);
            NetworkObject elsewhere = Create("Elsewhere");
            var host = new UnityServerSceneHost(registry)
            {
                FindSceneObjects = () => new List<NetworkObject> { inScene, elsewhere },
            };
            host.TryLoad(SceneId, () => true, () => { }, exception => { });
            host.Unload(SceneId, () => { });

            var found = new List<INetworkEntity>();
            host.PresentSceneObjects(found);

            CollectionAssert.AreEqual(new INetworkEntity[] { elsewhere }, found);

            loader.FinishUnload();
            found.Clear();
            host.PresentSceneObjects(found);

            CollectionAssert.AreEqual(new INetworkEntity[] { inScene, elsewhere }, found);
        }

        [Test]
        public void TryGetSceneIsAnsweredOnlyByTheUnitySceneHost()
        {
            var registry = new SceneRegistry();
            registry.Register(SceneId, new HeldLoader(scene));
            var unityHost = new UnityServerSceneHost(registry);
            unityHost.TryLoad(SceneId, () => true, () => { }, exception => { });

            Assert.IsTrue(Server(unityHost).Scenes.TryGetScene(SceneId, out Scene found));
            Assert.AreEqual(scene, found);
            Assert.IsFalse(Server(new OtherHost()).Scenes.TryGetScene(SceneId, out Scene none));
            Assert.AreEqual(default(Scene), none);
        }

        private static ServerManager Server(IServerSceneHost host)
        {
            MessageChannels channels = TestObjects.Channels();
            return new ServerManager(
                TestObjects.Schema(),
                new SessionLimits(),
                new SessionConfig(),
                TestBundles.Protocol(channels),
                TestObjects.Protocol(channels),
                TestObjects.StateProtocol(channels),
                TestObjects.TransformProtocol(channels),
                TestObjects.SceneProtocol(channels),
                TestObjects.ClockProtocol(),
                TestObjects.InputProtocol(),
                new RpcMessageIds(),
                null,
                8,
                null,
                host,
                new NetworkLog(exception => throw exception, message => { }));
        }

        private NetworkObject Create(string name)
        {
            var gameObject = new GameObject(name);
            created.Add(gameObject);
            return gameObject.AddComponent<NetworkObject>();
        }

        private sealed class HeldLoader : ISceneLoader
        {
            private readonly Scene scene;
            private Action unloaded;

            public HeldLoader(Scene scene)
            {
                this.scene = scene;
            }

            public void Load(Action<Scene> loaded, Action<Exception> failed) => loaded(scene);

            public void Unload(Scene unloading, Action done) => unloaded = done;

            public void FinishUnload() => unloaded();
        }

        private sealed class OtherHost : IServerSceneHost
        {
            public bool TryLoad(uint sceneId, Func<bool> accept, Action loaded, Action<Exception> failed) => false;

            public void Unload(uint sceneId, Action unloaded) => unloaded();

            public void SceneObjectsOf(uint sceneId, List<INetworkEntity> found)
            {
            }

            public bool Holds(uint sceneId, INetworkEntity entity) => false;

            public bool Knows(uint sceneId) => false;

            public void PresentSceneObjects(List<INetworkEntity> found)
            {
            }
        }
    }
}
