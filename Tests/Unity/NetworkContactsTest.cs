using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests
{
    public sealed class NetworkContactsTest
    {
        private const float StepSeconds = 0.05f;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<string> events = new List<string>();
        private Scene scene;
        private PhysicsWorlds worlds;
        private uint tick;

        [SetUp]
        public void CreateWorld()
        {
            PhysicsSimulationOwner.Acquire();
            scene = EditorSceneManager.NewPreviewScene();
            worlds = new PhysicsWorlds();
            worlds.Of(scene);
            worlds.Of2D(scene);
            tick = 1;
        }

        [TearDown]
        public void DestroyWorld()
        {
            foreach (GameObject gameObject in created)
            {
                if (gameObject != null)
                {
                    foreach (MonoBehaviour behaviour in gameObject.GetComponents<MonoBehaviour>())
                    {
                        Disable(behaviour);
                    }

                    Object.DestroyImmediate(gameObject);
                }
            }

            created.Clear();
            events.Clear();
            worlds.ReleaseStepping();
            EditorSceneManager.ClosePreviewScene(scene);
            PhysicsSimulationOwner.Release();
            Assert.AreEqual(0, PhysicsStepOwners.Count);
            Assert.AreEqual(0, ContactTrackers.Count);
        }

        [Test]
        public void ATrigger2DReportsOneEnterAndOneExitAsABodyPassesThrough()
        {
            NetworkTrigger2D trigger = Trigger2D(new Vector2(0f, 0f), new Vector2(1f, 1f));
            Rigidbody2D ball = Ball2D(new Vector2(-3f, 0f), new Vector2(5f, 0f));
            bool touchedInside = false;
            for (int step = 0; step < 40; step++)
            {
                Tick();
                touchedInside |= trigger.Touching.Contains(ball.GetComponent<Collider2D>());
            }

            CollectionAssert.AreEqual(new[] { "enter Ball2D", "exit Ball2D" }, events);
            Assert.IsTrue(touchedInside);
            Assert.AreEqual(0, trigger.Touching.Count);
        }

        [Test]
        public void ATriggerReportsOneEnterAndOneExitAsABodyPassesThrough()
        {
            NetworkTrigger trigger = Trigger(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
            Rigidbody ball = Ball(new Vector3(-3f, 0f, 0f), new Vector3(5f, 0f, 0f));
            bool touchedInside = false;
            for (int step = 0; step < 40; step++)
            {
                Tick();
                touchedInside |= trigger.Touching.Contains(ball.GetComponent<Collider>());
            }

            CollectionAssert.AreEqual(new[] { "enter Ball", "exit Ball" }, events);
            Assert.IsTrue(touchedInside);
            Assert.AreEqual(0f, trigger.AdditionalSize);
        }

        [Test]
        public void ATriggerSkipsAColliderItIgnores()
        {
            NetworkTrigger trigger = Trigger(new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f));
            Rigidbody ball = Ball(new Vector3(0f, 0f, 0f), Vector3.zero);
            Physics.IgnoreCollision(trigger.GetComponent<Collider>(), ball.GetComponent<Collider>());
            Ball(new Vector3(0.5f, 0f, 0f), Vector3.zero).name = "Other";

            Tick();

            CollectionAssert.AreEqual(new[] { "enter Other" }, events);
        }

        [Test]
        public void ACollision2DFindsABodyThatHitsItButNotATrigger()
        {
            var wall = Created("Wall");
            wall.transform.position = new Vector3(0f, 0f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 4f);
            NetworkCollision2D collision = Enable(wall.AddComponent<NetworkCollision2D>());
            Listen(collision);
            Ball2D(new Vector2(-3f, 0f), new Vector2(5f, 0f));
            Rigidbody2D ghost = Ball2D(new Vector2(0.4f, 1f), Vector2.zero);
            ghost.name = "Ghost";
            ghost.GetComponent<Collider2D>().isTrigger = true;

            for (int step = 0; step < 20; step++)
            {
                Tick();
            }

            CollectionAssert.AreEqual(new[] { "enter Ball2D" }, events);
            Assert.AreEqual(1, collision.Touching.Count);
        }

        [Test]
        public void ACollisionFindsABodyRestingOnItWithinTheContactOffset()
        {
            var floor = Created("Floor");
            floor.AddComponent<BoxCollider>().size = new Vector3(10f, 1f, 10f);
            NetworkCollision collision = Enable(floor.AddComponent<NetworkCollision>());
            Listen(collision);
            var crate = Created("Crate");
            crate.transform.position = new Vector3(0f, 1.2f, 0f);
            crate.AddComponent<BoxCollider>();
            crate.AddComponent<Rigidbody>();

            for (int step = 0; step < 40; step++)
            {
                Tick();
            }

            CollectionAssert.AreEqual(new[] { "enter Crate" }, events);
            Assert.AreEqual(Physics.defaultContactOffset * 2f, collision.AdditionalSize, 1e-6f);
            Assert.AreEqual(1f, crate.transform.position.y, 0.05f);
        }

        [Test]
        public void AnOtherColliderThatIsDisabledOrDestroyedLeavesWithAnExit()
        {
            NetworkTrigger2D trigger = Trigger2D(new Vector2(0f, 0f), new Vector2(4f, 4f));
            Rigidbody2D first = Ball2D(new Vector2(-1f, 0f), Vector2.zero);
            first.name = "First";
            Rigidbody2D second = Ball2D(new Vector2(1f, 0f), Vector2.zero);
            second.name = "Second";
            Collider2D secondCollider = second.GetComponent<Collider2D>();
            Collider2D exited = null;
            trigger.OnExit += other => exited = other;
            Tick();

            first.GetComponent<Collider2D>().enabled = false;
            Tick();
            Object.DestroyImmediate(second.gameObject);
            Tick();

            CollectionAssert.AreEquivalent(new[] { "enter First", "enter Second", "exit First", "exit destroyed" }, events);
            Assert.AreSame(secondCollider, exited);
            Assert.IsTrue(exited == null);
            Assert.AreEqual(0, trigger.Touching.Count);
        }

        [Test]
        public void DisablingTheComponentClearsItsContactsWithoutAnExitAndLeavesTheTracker()
        {
            NetworkTrigger2D trigger = Trigger2D(new Vector2(0f, 0f), new Vector2(4f, 4f));
            Ball2D(new Vector2(0f, 0f), Vector2.zero);
            Tick();

            Disable(trigger);

            CollectionAssert.AreEqual(new[] { "enter Ball2D" }, events);
            Assert.AreEqual(0, trigger.Touching.Count);
            Assert.IsNull(ContactTrackers.Of(scene.GetPhysicsScene2D()));
        }

        [Test]
        public void AComponentFollowsItsGameObjectIntoAnotherWorld()
        {
            Scene other = EditorSceneManager.NewPreviewScene();
            try
            {
                worlds.Of2D(other);
                NetworkTrigger2D trigger = Trigger2D(new Vector2(0f, 0f), new Vector2(1f, 1f));
                Tick();
                SceneManager.MoveGameObjectToScene(trigger.gameObject, other);

                Tick();

                Assert.IsNull(ContactTrackers.Of(scene.GetPhysicsScene2D()));
                Assert.IsNotNull(ContactTrackers.Of(other.GetPhysicsScene2D()));
                Disable(trigger);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(other);
            }
        }

        [Test]
        public void WithoutAStepOwnerUnityCallbacksPassThroughAndWithOneTheyAreIgnored()
        {
            NetworkTrigger2D trigger = Trigger2D(new Vector2(0f, 0f), new Vector2(1f, 1f));
            Collider2D ball = Ball2D(new Vector2(10f, 0f), Vector2.zero).GetComponent<Collider2D>();

            Invoke(trigger, "OnTriggerEnter2D", ball);

            CollectionAssert.AreEqual(new[] { "enter Ball2D" }, events);
            Assert.IsTrue(trigger.Touching.Contains(ball));

            Tick();
            Invoke(trigger, "OnTriggerEnter2D", ball);
            Invoke(trigger, "OnTriggerExit2D", ball);

            CollectionAssert.AreEqual(new[] { "enter Ball2D", "exit Ball2D" }, events);
        }

        [Test]
        public void TheHistoryRestoresAKeptTickAndThePublishSendsOnlyTheDifference()
        {
            NetworkTrigger2D trigger = Trigger2D(new Vector2(0f, 0f), new Vector2(1f, 1f));
            Rigidbody2D ball = Ball2D(new Vector2(-3f, 0f), new Vector2(5f, 0f));
            ContactTracker<Collider2D> tracker = ContactTrackers.Of(scene.GetPhysicsScene2D());
            uint inside = 0;
            for (int step = 0; step < 40; step++)
            {
                PhysicsSteps.StepAndPublish(worlds, StepSeconds, tick, true, 64);
                if (inside == 0 && trigger.Touching.Count > 0)
                {
                    inside = tick;
                }

                tick++;
            }

            events.Clear();
            tracker.Restore(tick + 100);
            tracker.Publish();

            CollectionAssert.IsEmpty(events);

            tracker.Restore(inside);

            Assert.AreEqual(0, trigger.Touching.Count);

            tracker.Publish();

            CollectionAssert.AreEqual(new[] { "enter Ball2D" }, events);
            Assert.IsTrue(trigger.Touching.Contains(ball.GetComponent<Collider2D>()));
        }

        [Test]
        public void AnAttachedTriggerTakesItsContactsFromItsOwnerAndIgnoresUnityCallbacks()
        {
            NetworkTrigger trigger = Trigger(Vector3.zero, Vector3.one);
            Collider own = trigger.GetComponent<Collider>();
            Collider ball = Ball(new Vector3(50f, 0f, 0f), Vector3.zero).GetComponent<Collider>();
            var tracker = new ContactTracker<Collider>();
            var query = new FixedQuery();
            query.Touching.Add(ball);
            query.Touching.Add(own);

            Assert.IsTrue(trigger.Attach(tracker, query));

            Assert.IsTrue(trigger.IsAttached);
            Assert.AreEqual(1, tracker.Count);
            Assert.IsNull(ContactTrackers.Of(scene.GetPhysicsScene()));
            Invoke(trigger, "OnTriggerEnter", ball);
            CollectionAssert.IsEmpty(events);

            tracker.Query();
            tracker.Publish();

            CollectionAssert.AreEqual(new[] { "enter Ball" }, events);
            CollectionAssert.AreEqual(new[] { own }, query.Asked);

            query.Touching.Clear();
            tracker.Query();
            tracker.Publish();

            CollectionAssert.AreEqual(new[] { "enter Ball", "exit Ball" }, events);
        }

        [Test]
        public void TheFirstOwnerKeepsAComponentAndDetachingReturnsItToItsPhysicsScene()
        {
            GameObject crate = Created("Crate2D");
            crate.AddComponent<BoxCollider2D>();
            NetworkCollision2D collision = Enable(crate.AddComponent<NetworkCollision2D>());
            Listen(collision);
            Collider2D ball = Ball2D(new Vector2(10f, 0f), Vector2.zero).GetComponent<Collider2D>();
            var first = new ContactTracker<Collider2D>();
            var second = new ContactTracker<Collider2D>();
            var query = new FixedQuery();
            var other = new FixedQuery();
            query.Touching2D.Add(ball);

            Assert.IsTrue(collision.Attach(first, query));
            Assert.IsFalse(collision.Attach(second, other));
            first.Query();
            first.Publish();

            CollectionAssert.AreEqual(new[] { "enter Ball2D" }, events);

            collision.Detach(other);

            Assert.IsTrue(collision.IsAttached);
            Assert.AreEqual((1, 0), (first.Count, second.Count));

            collision.Detach(query);

            Assert.IsFalse(collision.IsAttached);
            Assert.AreEqual(0, first.Count);
            Assert.AreEqual(0, collision.Touching.Count);
            CollectionAssert.AreEqual(new[] { "enter Ball2D" }, events);
            Assert.IsNotNull(ContactTrackers.Of(scene.GetPhysicsScene2D()));
            Assert.Throws<System.ArgumentNullException>(() => collision.Attach(null, query));
            Assert.Throws<System.ArgumentNullException>(() => collision.Attach(first, null));
            Assert.Throws<System.ArgumentNullException>(() => collision.Detach(null));
        }

        [Test]
        public void AComponentAttachedWhileDisabledJoinsItsOwnerWhenEnabled()
        {
            GameObject wall = Created("Wall");
            wall.AddComponent<BoxCollider>();
            var collision = wall.AddComponent<NetworkCollision>();
            GameObject zone = Created("Zone2D");
            zone.AddComponent<BoxCollider2D>().isTrigger = true;
            var trigger = zone.AddComponent<NetworkTrigger2D>();
            var tracker = new ContactTracker<Collider>();
            var tracker2D = new ContactTracker<Collider2D>();
            var query = new FixedQuery();

            Assert.IsTrue(collision.Attach(tracker, query));
            Assert.IsTrue(trigger.Attach(tracker2D, query));

            Assert.AreEqual((0, 0), (tracker.Count, tracker2D.Count));

            Enable(collision);
            Enable(trigger);

            Assert.AreEqual((1, 1), (tracker.Count, tracker2D.Count));
            Assert.AreEqual(0, ContactTrackers.Count);

            Disable(collision);
            Disable(trigger);

            Assert.AreEqual((0, 0), (tracker.Count, tracker2D.Count));
        }

        private void Tick()
        {
            PhysicsSteps.StepAndPublish(worlds, StepSeconds, tick, false, 64);
            tick++;
        }

        private NetworkTrigger2D Trigger2D(Vector2 position, Vector2 size)
        {
            GameObject zone = Created("Zone2D");
            zone.transform.position = position;
            var box = zone.AddComponent<BoxCollider2D>();
            box.size = size;
            box.isTrigger = true;
            NetworkTrigger2D trigger = Enable(zone.AddComponent<NetworkTrigger2D>());
            trigger.OnEnter += other => events.Add("enter " + Name(other));
            trigger.OnExit += other => events.Add("exit " + Name(other));
            return trigger;
        }

        private NetworkTrigger Trigger(Vector3 position, Vector3 size)
        {
            GameObject zone = Created("Zone");
            zone.transform.position = position;
            var box = zone.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true;
            NetworkTrigger trigger = Enable(zone.AddComponent<NetworkTrigger>());
            trigger.OnEnter += other => events.Add("enter " + Name(other));
            trigger.OnExit += other => events.Add("exit " + Name(other));
            return trigger;
        }

        private void Listen(NetworkCollision2D collision)
        {
            collision.OnEnter += other => events.Add("enter " + Name(other));
            collision.OnExit += other => events.Add("exit " + Name(other));
        }

        private void Listen(NetworkCollision collision)
        {
            collision.OnEnter += other => events.Add("enter " + Name(other));
            collision.OnExit += other => events.Add("exit " + Name(other));
        }

        private Rigidbody2D Ball2D(Vector2 position, Vector2 velocity)
        {
            GameObject ball = Created("Ball2D");
            ball.transform.position = position;
            ball.AddComponent<CircleCollider2D>().radius = 0.25f;
            var rigidbody = ball.AddComponent<Rigidbody2D>();
            rigidbody.gravityScale = 0f;
            rigidbody.position = position;
            rigidbody.linearVelocity = velocity;
            return rigidbody;
        }

        private Rigidbody Ball(Vector3 position, Vector3 velocity)
        {
            GameObject ball = Created("Ball");
            ball.transform.position = position;
            ball.AddComponent<SphereCollider>().radius = 0.25f;
            var rigidbody = ball.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.position = position;
            rigidbody.linearVelocity = velocity;
            return rigidbody;
        }

        private GameObject Created(string name)
        {
            var gameObject = new GameObject(name);
            created.Add(gameObject);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private static string Name(Component other) => other == null ? "destroyed" : other.name;

        private static T Enable<T>(T behaviour)
            where T : MonoBehaviour
        {
            Invoke(behaviour, "OnEnable");
            return behaviour;
        }

        private static void Disable(MonoBehaviour behaviour) => Invoke(behaviour, "OnDisable");

        private static void Invoke(MonoBehaviour behaviour, string message, params object[] arguments) =>
            behaviour.GetType().GetMethod(message, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(behaviour, arguments);

        private sealed class FixedQuery : IContactQuery, IContactQuery2D
        {
            public readonly HashSet<Collider> Touching = new HashSet<Collider>();
            public readonly HashSet<Collider2D> Touching2D = new HashSet<Collider2D>();
            public readonly List<Component> Asked = new List<Component>();

            public void Collect(Collider own, HashSet<Collider> into)
            {
                Asked.Add(own);
                into.UnionWith(Touching);
            }

            public void Collect(Collider2D own, HashSet<Collider2D> into)
            {
                Asked.Add(own);
                into.UnionWith(Touching2D);
            }
        }
    }
}
