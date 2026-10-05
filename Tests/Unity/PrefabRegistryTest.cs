using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class PrefabRegistryTest
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (UnityEngine.Object value in created)
            {
                UnityEngine.Object.DestroyImmediate(value);
            }

            created.Clear();
            TestPrefabs.DestroyAllInScene();
        }

        [Test]
        public void Fnv1aMatchesTheReferenceValues()
        {
            Assert.AreEqual(0x811C9DC5u, PrefabHash.Fnv1a(""));
            Assert.AreEqual(0xE40C292Cu, PrefabHash.Fnv1a("a"));
            Assert.AreEqual(0xBF9CF968u, PrefabHash.Fnv1a("foobar"));
        }

        [Test]
        public void FingerprintHashesTheBehaviourTypeNamesInHierarchyOrder()
        {
            NetworkObject prefab = TestPrefabs.Create("Prefab", 0xA1);
            var child = new GameObject("Child");
            child.transform.SetParent(prefab.transform);
            child.AddComponent<SilentBehaviour>();
            var registry = new PrefabRegistry();

            registry.Register(prefab);

            Assert.AreEqual(0xD5D1EDCDu, registry.FingerprintOf(0xA1));
        }

        [Test]
        public void RegisteringTheSamePrefabAgainIsIgnored()
        {
            NetworkObject prefab = TestPrefabs.Create("Prefab", 0xA1);
            var registry = new PrefabRegistry();

            registry.Register(prefab);
            registry.Register(prefab);

            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void AnotherPrefabWithARegisteredIdThrows()
        {
            var registry = new PrefabRegistry();
            registry.Register(TestPrefabs.Create("First", 0xA1));

            ArgumentException error = Assert.Throws<ArgumentException>(() => registry.Register(TestPrefabs.Create("Second", 0xA1)));
            StringAssert.Contains("First", error.Message);
            Assert.Throws<ArgumentException>(() => registry.Register(0xA1, 1, (position, rotation) => null));
        }

        [Test]
        public void InvalidPrefabsThrow()
        {
            var registry = new PrefabRegistry();
            NetworkObject nested = TestPrefabs.Create("Nested", 0xA2);
            TestPrefabs.Create("Inner", 0xA3).transform.SetParent(nested.transform);
            NetworkObject child = TestPrefabs.Create("Child", 0xA4);
            child.transform.SetParent(new GameObject("Root").transform);
            NetworkObject crowded = TestPrefabs.Create("Crowded", 0xA5);
            for (int index = 0; index < PrefabHash.MaxBehaviours; index++)
            {
                crowded.gameObject.AddComponent<SilentBehaviour>();
            }

            Assert.Throws<ArgumentNullException>(() => registry.Register(null));
            Assert.Throws<ArgumentException>(() => registry.Register(TestPrefabs.Create("NoId", 0)));
            StringAssert.Contains("nested", Assert.Throws<ArgumentException>(() => registry.Register(nested)).Message);
            StringAssert.Contains("root", Assert.Throws<ArgumentException>(() => registry.Register(child)).Message);
            StringAssert.Contains("257", Assert.Throws<ArgumentException>(() => registry.Register(crowded)).Message);
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void FactoryRegistration()
        {
            var registry = new PrefabRegistry();
            Func<Vector3, Quaternion, NetworkObject> create = (position, rotation) => null;
            Action<NetworkObject> release = instance => { };

            registry.Register(0xB1, 0xF1, create, release);
            registry.Register(0xB1, 0xF1, create, release);

            Assert.AreEqual(1, registry.Count);
            Assert.AreEqual(0xF1u, registry.FingerprintOf(0xB1));
            Assert.Throws<ArgumentException>(() => registry.Register(0xB1, 0xF2, create, release));
            Assert.Throws<ArgumentException>(() => registry.Register(0, 0xF1, create));
            Assert.Throws<ArgumentNullException>(() => registry.Register(0xB2, 0xF1, null));
            Assert.Throws<ArgumentException>(() => registry.Register(TestPrefabs.Create("Prefab", 0xB1)));
        }

        [Test]
        public void NetworkManagerRegistersTheCollectedAndTheManualPrefabs()
        {
            NetworkObject first = TestPrefabs.Create("First", 0xA1);
            NetworkObject second = TestPrefabs.Create("Second", 0xA2);
            NetworkObject third = TestPrefabs.Create("Third", 0xA3);

            NetworkManager manager = CreateManager(true, new[] { first, second }, new[] { second, third, null });

            Assert.AreEqual(3, manager.Prefabs.Count);
            Assert.IsTrue(manager.Prefabs.Contains(0xA1));
            Assert.IsTrue(manager.Prefabs.Contains(0xA3));
        }

        [Test]
        public void TurningCollectionOffKeepsOnlyTheManualPrefabs()
        {
            NetworkObject first = TestPrefabs.Create("First", 0xA1);
            NetworkObject second = TestPrefabs.Create("Second", 0xA2);

            NetworkManager manager = CreateManager(false, new[] { first }, new[] { second });

            Assert.AreEqual(1, manager.Prefabs.Count);
            Assert.IsTrue(manager.Prefabs.Contains(0xA2));
        }

        [Test]
        public void ManualPrefabWithACollectedIdFailsInitialize()
        {
            NetworkObject first = TestPrefabs.Create("First", 0xA1);
            NetworkObject clash = TestPrefabs.Create("Clash", 0xA1);

            Assert.Throws<ArgumentException>(() => CreateManager(true, new[] { first }, new[] { clash }));
        }

        private NetworkManager CreateManager(bool collectPrefabs, NetworkObject[] collected, NetworkObject[] manual)
        {
            var list = ScriptableObject.CreateInstance<NetworkPrefabList>();
            list.Set(collected);
            created.Add(list);
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            created.Add(manager.gameObject);
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("collectPrefabs").boolValue = collectPrefabs;
            serialized.FindProperty("collectedPrefabs").objectReferenceValue = list;
            SerializedProperty prefabs = serialized.FindProperty("prefabs");
            prefabs.arraySize = manual.Length;
            for (int index = 0; index < manual.Length; index++)
            {
                prefabs.GetArrayElementAtIndex(index).objectReferenceValue = manual[index];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            return manager;
        }
    }
}
