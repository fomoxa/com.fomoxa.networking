using System;
using System.Collections.Generic;
using System.Numerics;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ServerSceneContentTest
    {
        private const uint SceneId = 5;
        private const uint OtherSceneId = 6;

        [Test]
        public void LoadedContentSpawnsItsSceneObjectsThenReportsTheScene()
        {
            var rig = new Rig();
            rig.Scenes.LoadGlobal(new[] { SceneId });

            Assert.IsTrue(rig.Content.IsLoading(SceneId));
            SceneEntity door = rig.Host.Complete(0)[0];

            Assert.IsTrue(rig.Content.IsLoaded(SceneId));
            Assert.IsNotNull(door.Record);
            CollectionAssert.AreEqual(new[] { "loaded 5" }, rig.Events);
        }

        [Test]
        public void AFailedLoadIsReportedWithItsExceptionAndSpawnsNothing()
        {
            var rig = new Rig();
            rig.Scenes.LoadGlobal(new[] { SceneId });
            var failure = new InvalidOperationException("missing");

            rig.Host.Fail(0, failure);

            Assert.IsFalse(rig.Content.IsLoading(SceneId));
            Assert.IsFalse(rig.Content.IsLoaded(SceneId));
            CollectionAssert.AreEqual(new[] { "failed 5 missing" }, rig.Events);
            CollectionAssert.AreEqual(new[] { failure }, rig.Logged);
        }

        [Test]
        public void ASceneThatLeavesWhileLoadingRejectsTheLoad()
        {
            var rig = new Rig();
            rig.Scenes.LoadGlobal(new[] { SceneId });
            rig.Scenes.UnloadGlobal(new[] { SceneId });

            List<SceneEntity> content = rig.Host.Complete(0);

            Assert.IsNull(content[0].Record);
            Assert.IsFalse(rig.Content.IsLoaded(SceneId));
            CollectionAssert.AreEqual(new[] { content }, rig.Host.Discarded);
            CollectionAssert.IsEmpty(rig.Events);
        }

        [Test]
        public void ASceneThatReturnsWhileUnloadingLoadsAfterTheUnload()
        {
            var rig = new Rig();
            rig.Scenes.LoadGlobal(new[] { SceneId });
            rig.Host.Complete(0);
            rig.Scenes.UnloadGlobal(new[] { SceneId });
            rig.Scenes.LoadGlobal(new[] { SceneId });

            Assert.AreEqual(1, rig.Host.Loads.Count);

            rig.Host.FinishUnload(0);

            Assert.AreEqual(2, rig.Host.Loads.Count);
        }

        [Test]
        public void LeavingAgainBeforeTheUnloadFinishesCancelsTheReload()
        {
            var rig = new Rig();
            rig.Scenes.LoadGlobal(new[] { SceneId });
            rig.Host.Complete(0);
            rig.Scenes.UnloadGlobal(new[] { SceneId });
            rig.Scenes.LoadGlobal(new[] { SceneId });
            rig.Scenes.UnloadGlobal(new[] { SceneId });

            rig.Host.FinishUnload(0);

            Assert.AreEqual(1, rig.Host.Loads.Count);
        }

        [Test]
        public void AStaleLoadAfterARestartIsDiscardedAndTheNewContentKept()
        {
            var rig = new Rig();
            rig.Scenes.LoadGlobal(new[] { SceneId });
            rig.Restart();
            rig.Scenes.LoadGlobal(new[] { SceneId });
            List<SceneEntity> fresh = rig.Host.Complete(1);

            List<SceneEntity> stale = rig.Host.Complete(0);

            CollectionAssert.AreEqual(new[] { stale }, rig.Host.Discarded);
            Assert.IsNull(stale[0].Record);
            Assert.IsNotNull(fresh[0].Record);
            var found = new List<INetworkEntity>();
            rig.Host.SceneObjectsOf(SceneId, found);
            CollectionAssert.AreEqual(fresh, found);

            rig.Scenes.UnloadGlobal(new[] { SceneId });

            CollectionAssert.AreEqual(new[] { fresh }, rig.Host.Unloading);
        }

        [Test]
        public void StoppingTheServerUnloadsEveryLoadedScene()
        {
            var rig = new Rig();
            rig.Scenes.LoadGlobal(new[] { SceneId, OtherSceneId });
            List<SceneEntity> first = rig.Host.Complete(0);
            List<SceneEntity> second = rig.Host.Complete(1);

            rig.Session.Stop();

            CollectionAssert.AreEquivalent(new[] { first, second }, rig.Host.Unloading);
            Assert.IsFalse(rig.Content.IsLoaded(SceneId));
            Assert.IsFalse(rig.Content.IsLoaded(OtherSceneId));
        }

        private sealed class Rig
        {
            public readonly List<string> Events = new List<string>();
            public readonly List<Exception> Logged = new List<Exception>();
            public readonly FakeHost Host = new FakeHost();
            public readonly ServerSession Session;
            public readonly ServerObjects Objects;
            public readonly ServerEntities Entities;
            public readonly ServerSceneContent Content;

            public Rig()
            {
                var dispatcher = new MessageDispatcher(TestObjects.Schema());
                Session = new ServerSession(TestObjects.Schema(), new SessionConfig(), new SessionLimits(), dispatcher, TestBundles.Protocol(TestObjects.Channels()));
                Objects = new ServerObjects(Session, TestObjects.Protocol(TestObjects.Channels()), objectId => Entities.ReadSpawnData(objectId), TestObjects.SceneProtocol(TestObjects.Channels()));
                var clock = new ServerClock(Session, TestObjects.ClockProtocol());
                var inputs = new ServerInputs(Session, Objects, TestObjects.InputProtocol(), clock);
                var log = new NetworkLog(Logged.Add, message => { });
                Entities = new ServerEntities(
                    this,
                    Session,
                    Objects,
                    inputs,
                    dispatcher,
                    TestObjects.Channels(),
                    TestObjects.StateProtocol(TestObjects.Channels()),
                    TestObjects.InputProtocol(),
                    TestObjects.TransformProtocol(TestObjects.Channels()),
                    new RpcMessageIds(),
                    new SceneBackend(Host),
                    log);
                Content = new ServerSceneContent(Session, Objects.Scenes, Entities, Host, log);
                Content.OnLoaded += sceneId => Events.Add($"loaded {sceneId}");
                Content.OnLoadFailed += (sceneId, exception) => Events.Add($"failed {sceneId} {exception.Message}");
                Session.Start(new LoopbackListener(8));
            }

            public ServerScenes Scenes => Objects.Scenes;

            public void Restart()
            {
                Session.Stop();
                Session.Start(new LoopbackListener(8));
            }
        }

        private sealed class FakeHost : IServerSceneHost
        {
            public readonly List<Load> Loads = new List<Load>();
            public readonly List<List<SceneEntity>> Discarded = new List<List<SceneEntity>>();
            public readonly List<List<SceneEntity>> Unloading = new List<List<SceneEntity>>();
            private readonly Dictionary<uint, List<SceneEntity>> accepted = new Dictionary<uint, List<SceneEntity>>();
            private readonly List<Action> unloaded = new List<Action>();

            public bool TryLoad(uint sceneId, Func<bool> accept, Action loaded, Action<Exception> failed)
            {
                Loads.Add(new Load(sceneId, accept, loaded, failed));
                return true;
            }

            public List<SceneEntity> Complete(int index)
            {
                Load load = Loads[index];
                var content = new List<SceneEntity> { new SceneEntity(load.SceneId, (ulong)(index + 1)) };
                if (!load.Accept())
                {
                    Discarded.Add(content);
                    return content;
                }

                accepted.Add(load.SceneId, content);
                load.Loaded();
                return content;
            }

            public void Fail(int index, Exception exception) => Loads[index].Failed(exception);

            public void FinishUnload(int index) => unloaded[index]();

            public void Unload(uint sceneId, Action done)
            {
                if (!accepted.Remove(sceneId, out List<SceneEntity> content))
                {
                    done();
                    return;
                }

                Unloading.Add(content);
                unloaded.Add(done);
            }

            public void SceneObjectsOf(uint sceneId, List<INetworkEntity> found)
            {
                if (accepted.TryGetValue(sceneId, out List<SceneEntity> content))
                {
                    found.AddRange(content);
                }
            }

            public bool Holds(uint sceneId, INetworkEntity entity) =>
                accepted.TryGetValue(sceneId, out List<SceneEntity> content) && content.Contains((SceneEntity)entity);

            public uint SceneOf(INetworkEntity entity) => ((SceneEntity)entity).SceneId;
        }

        private sealed class Load
        {
            public Load(uint sceneId, Func<bool> accept, Action loaded, Action<Exception> failed)
            {
                SceneId = sceneId;
                Accept = accept;
                Loaded = loaded;
                Failed = failed;
            }

            public uint SceneId { get; }

            public Func<bool> Accept { get; }

            public Action Loaded { get; }

            public Action<Exception> Failed { get; }
        }

        private sealed class SceneBackend : IServerEntityBackend
        {
            private readonly FakeHost host;

            public SceneBackend(FakeHost host)
            {
                this.host = host;
            }

            public string NameOf(INetworkEntity entity) => "scene object";

            public void ValidateSpawn(INetworkEntity entity)
            {
            }

            public uint FingerprintOf(INetworkEntity entity, bool isSceneObject) => 0xF1;

            public uint SceneIdOf(INetworkEntity entity) => host.SceneOf(entity);

            public void PrepareSpawn(INetworkEntity entity)
            {
            }

            public void Activate(INetworkEntity entity)
            {
            }

            public void End(INetworkEntity entity, bool isSceneObject)
            {
            }
        }

        private sealed class SceneEntity : INetworkEntity
        {
            public SceneEntity(uint sceneId, ulong sceneObjectId)
            {
                SceneId = sceneId;
                SceneObjectId = ((ulong)sceneId << 32) | sceneObjectId;
            }

            public uint SceneId { get; }

            public uint PrefabId => 0;

            public ulong SceneObjectId { get; }

            public bool DespawnWithOwner => true;

            public NetworkVisibility Visibility => NetworkVisibility.Rule;

            public IReadOnlyList<EntityBehaviour> EntityBehaviours => Array.Empty<EntityBehaviour>();

            public EntityRecord Record { get; private set; }

            public Vector3 ReadWorldPosition() => Vector3.Zero;

            public void ReadRootPose(out Vector3 worldPosition, out Quaternion worldRotation, out Vector3 localScale)
            {
                worldPosition = Vector3.Zero;
                worldRotation = Quaternion.Identity;
                localScale = Vector3.One;
            }

            public void Bind(EntityRecord record) => Record = record;

            public void Unbind(EntityRecord record)
            {
                if (Record == record)
                {
                    Record = null;
                }
            }
        }
    }
}
