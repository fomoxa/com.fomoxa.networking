using System.Collections.Generic;
using System.Text.RegularExpressions;
using Fomoxa.Unity.Editor;
using NUnit.Framework;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Tests
{
    public sealed class FomoxaSceneObjectIdsTest
    {
        [TearDown]
        public void DestroyObjects()
        {
            TestPrefabs.DestroyAllInScene();
        }

        [Test]
        public void SceneHashIsTheFnv1aOfTheSceneGuid()
        {
            Assert.AreEqual(0x86D04DB5u, FomoxaSceneObjectIds.SceneHashOf("0123456789abcdef0123456789abcdef"));
            Assert.AreEqual(0u, FomoxaSceneObjectIds.SceneHashOf(""));
        }

        [Test]
        public void AssignKeepsTheFirstOfRepeatedIdsAndGivesTheOthersNewOnes()
        {
            NetworkObject missing = TestPrefabs.CreateSceneObject("Missing", 0);
            NetworkObject first = TestPrefabs.CreateSceneObject("First", 5);
            NetworkObject copy = TestPrefabs.CreateSceneObject("Copy", 5);

            Assert.IsTrue(FomoxaSceneObjectIds.Assign(SceneManager.GetActiveScene(), 0xABCD1234));

            Assert.AreEqual(0xABCD1234_00000005UL, first.SceneObjectId);
            Assert.AreEqual(0xABCD1234u, (uint)(missing.SceneObjectId >> 32));
            Assert.AreEqual(0xABCD1234u, (uint)(copy.SceneObjectId >> 32));
            var localIds = new HashSet<uint> { (uint)missing.SceneObjectId, (uint)first.SceneObjectId, (uint)copy.SceneObjectId };
            Assert.AreEqual(3, localIds.Count);
            Assert.IsFalse(localIds.Contains(0));
            Assert.IsFalse(FomoxaSceneObjectIds.Assign(SceneManager.GetActiveScene(), 0xABCD1234));
        }

        [Test]
        public void SetSceneHashChangesOnlyTheHighBits()
        {
            NetworkObject door = TestPrefabs.CreateSceneObject("Door", 0x11111111_00000007);
            NetworkObject unassigned = TestPrefabs.CreateSceneObject("Unassigned", 0);

            FomoxaSceneObjectIds.SetSceneHash(SceneManager.GetActiveScene(), 0x22222222);

            Assert.AreEqual(0x22222222_00000007UL, door.SceneObjectId);
            Assert.AreEqual(0UL, unassigned.SceneObjectId);
        }

        [Test]
        public void ValidateReportsMissingRepeatedAndNestedIds()
        {
            TestPrefabs.CreateSceneObject("Door", 7);
            TestPrefabs.CreateSceneObject("Copy", 7);
            TestPrefabs.CreateSceneObject("Missing", 0);
            NetworkObject outer = TestPrefabs.CreateSceneObject("Outer", 8);
            TestPrefabs.CreateSceneObject("Inner", 9).transform.SetParent(outer.transform);

            List<string> errors = FomoxaSceneObjectIds.Validate(SceneManager.GetActiveScene());

            Assert.AreEqual(4, errors.Count);
            Assert.IsTrue(errors.Exists(error => error.Contains("Copy has the scene object id of Door")));
            Assert.IsTrue(errors.Exists(error => error.Contains("Missing has no scene object id")));
            Assert.IsTrue(errors.Exists(error => error.Contains("Outer:") && error.Contains("nested")));
            Assert.IsTrue(errors.Exists(error => error.Contains("Outer/Inner:") && error.Contains("nested")));
        }

        [Test]
        public void ProcessingFailsTheBuildButFillsIdsForPlayMode()
        {
            NetworkObject missing = TestPrefabs.CreateSceneObject("Missing", 0);

            Assert.Throws<BuildFailedException>(() => FomoxaSceneObjectIds.ProcessScene(SceneManager.GetActiveScene(), true));
            Assert.AreEqual(0UL, missing.SceneObjectId);

            LogAssert.Expect(LogType.Warning, new Regex("for this Play Mode session only"));
            FomoxaSceneObjectIds.ProcessScene(SceneManager.GetActiveScene(), false);
            Assert.AreNotEqual(0u, (uint)missing.SceneObjectId);
        }
    }
}
