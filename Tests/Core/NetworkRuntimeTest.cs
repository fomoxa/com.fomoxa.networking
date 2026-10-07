using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class NetworkRuntimeTest
    {
        private const double OneTick = 1.0 / 30;

        [Test]
        public void BeginningAFrameTwiceThrowsAndLeavesTheFrameOpen()
        {
            NetworkRuntime runtime = Create();
            runtime.BeginFrame(TimeSpan.Zero, OneTick);

            Assert.Throws<InvalidOperationException>(() => runtime.BeginFrame(TimeSpan.Zero, OneTick));

            Assert.DoesNotThrow(() => runtime.EndFrame());
        }

        [Test]
        public void EndingAFrameThatIsNotOpenThrowsAndLeavesTheFrameClosed()
        {
            NetworkRuntime runtime = Create();

            Assert.Throws<InvalidOperationException>(() => runtime.EndFrame());

            Assert.DoesNotThrow(() => runtime.BeginFrame(TimeSpan.Zero, OneTick));
            Assert.DoesNotThrow(() => runtime.EndFrame());
            Assert.Throws<InvalidOperationException>(() => runtime.EndFrame());
        }

        [Test]
        public void CallsFromInsideATickThrow()
        {
            NetworkRuntime runtime = Create();
            var refused = new List<Exception>();
            runtime.TimeManager.OnTick += () =>
            {
                refused.Add(Catch(() => runtime.BeginFrame(TimeSpan.Zero, OneTick)));
                refused.Add(Catch(() => runtime.EndFrame()));
            };

            runtime.BeginFrame(TimeSpan.Zero, OneTick);

            Assert.AreEqual(2, refused.Count);
            Assert.IsInstanceOf<InvalidOperationException>(refused[0]);
            Assert.IsInstanceOf<InvalidOperationException>(refused[1]);
            Assert.DoesNotThrow(() => runtime.EndFrame());
        }

        [Test]
        public void AnExceptionInsideAFrameDoesNotStopTheFrameFromClosingOrTheNextFromOpening()
        {
            NetworkRuntime runtime = Create();
            bool fail = true;
            runtime.TimeManager.OnTick += () =>
            {
                if (fail)
                {
                    throw new InvalidOperationException("tick failed");
                }
            };

            Assert.Throws<InvalidOperationException>(() => runtime.BeginFrame(TimeSpan.Zero, OneTick));
            Assert.DoesNotThrow(() => runtime.EndFrame());

            fail = false;
            Assert.DoesNotThrow(() => runtime.BeginFrame(TimeSpan.Zero, OneTick));
            Assert.DoesNotThrow(() => runtime.EndFrame());
        }

        [Test]
        public void AttachedPhysicsIsSteppedEachTickAndDetachedPhysicsIsNot()
        {
            NetworkRuntime runtime = Create();
            var physics = new SteppedWorlds(PhysicsBackend.Rigidbody);

            runtime.Physics = physics;
            runtime.BeginFrame(TimeSpan.Zero, OneTick);
            runtime.EndFrame();
            runtime.Physics = null;
            runtime.BeginFrame(TimeSpan.Zero, OneTick);
            runtime.EndFrame();

            Assert.AreEqual(1, physics.World.Steps);
        }

        [Test]
        public void PhysicsCanBeAttachedBetweenBeginFrameAndEndFrame()
        {
            NetworkRuntime runtime = Create();
            var physics = new SteppedWorlds(PhysicsBackend.Rigidbody);
            runtime.BeginFrame(TimeSpan.Zero, OneTick);

            Assert.DoesNotThrow(() => runtime.Physics = physics);
            Assert.DoesNotThrow(() => runtime.EndFrame());
            Assert.AreSame(physics, runtime.Physics);
        }

        [Test]
        public void AttachingPhysicsFromInsideATickThrowsAndKeepsThePreviousPhysics()
        {
            NetworkRuntime runtime = Create();
            var physics = new SteppedWorlds(PhysicsBackend.Rigidbody);
            var refused = new List<Exception>();
            runtime.TimeManager.OnTick += () => refused.Add(Catch(() => runtime.Physics = physics));

            runtime.BeginFrame(TimeSpan.Zero, OneTick);
            runtime.EndFrame();

            Assert.AreEqual(1, refused.Count);
            Assert.IsInstanceOf<InvalidOperationException>(refused[0]);
            Assert.IsNull(runtime.Physics);
        }

        [Test]
        public void AttachingPhysicsOfAnotherBackendThrowsAndKeepsThePreviousPhysics()
        {
            NetworkRuntime runtime = Create();

            Assert.Throws<ArgumentException>(() => runtime.Physics = new SteppedWorlds(PhysicsBackend.Rapier));
            Assert.IsNull(runtime.Physics);
        }

        [Test]
        public void VersionMatchesThePackageManifest()
        {
            string manifest = File.ReadAllText(PackageManifestPath());
            Match version = Regex.Match(manifest, "\"version\"\\s*:\\s*\"([^\"]+)\"");

            Assert.IsTrue(version.Success);
            Assert.AreEqual(version.Groups[1].Value, NetworkRuntime.Version);
        }

        private static string PackageManifestPath([CallerFilePath] string testFile = "") =>
            Path.Combine(Path.GetDirectoryName(testFile), "..", "..", "package.json");

        private static Exception Catch(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private sealed class SteppedWorlds : IPhysicsWorlds
        {
            public SteppedWorlds(PhysicsBackend backend)
            {
                Backend = backend;
            }

            public PhysicsBackend Backend { get; }

            public ClientPredictionTest.FakeWorld World { get; } = new ClientPredictionTest.FakeWorld();

            public void WorldsToStep(List<IPhysicsSimulation> worlds) => worlds.Add(World);

            public IContactTracker TrackerOf(IPhysicsSimulation world) => null;
        }

        private static NetworkRuntime Create()
        {
            var host = new ServerSceneContentTest.FakeHost();
            var backends = new NetworkBackends(
                new ServerSceneContentTest.SceneBackend(host),
                host,
                new ClientEntitiesTest.FakeClientBackend(new List<string>()),
                new ClientManagerTest.NoScenes(),
                new ClientPredictionTest.FakeBackend());
            return new NetworkRuntime(
                TestObjects.Registry(),
                new NetworkSettings(),
                new ClientManagerTest.Factory(),
                backends,
                () => TimeSpan.Zero,
                new NetworkLog(exception => throw exception, message => { }));
        }
    }
}
