using System;
using System.Collections.Generic;
using BundleFixture;
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
