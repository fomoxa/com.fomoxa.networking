using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class EntityBehaviourAttachTest
    {
        [Test]
        public void AttachGivesEachBehaviourItsIndexBeforeTheEntityIsSpawned()
        {
            var behaviours = new EntityBehaviour[] { new PlainBehaviour(), new PlainBehaviour(), new PlainBehaviour() };

            EntityBehaviour.Attach(new ListEntity(behaviours));

            for (int index = 0; index < behaviours.Length; index++)
            {
                Assert.AreEqual(index, behaviours[index].BehaviourIndex);
                Assert.IsFalse(behaviours[index].IsReplaying);
                Assert.IsFalse(behaviours[index].SpawnedOnServer);
            }
        }

        [Test]
        public void AttachTakesUpTo256Behaviours()
        {
            var behaviours = new EntityBehaviour[256];
            for (int index = 0; index < behaviours.Length; index++)
            {
                behaviours[index] = new PlainBehaviour();
            }

            EntityBehaviour.Attach(new ListEntity(behaviours));

            Assert.AreEqual(255, behaviours[255].BehaviourIndex);
        }

        [Test]
        public void AttachRefusesMoreThan256Behaviours()
        {
            var behaviours = new EntityBehaviour[257];
            for (int index = 0; index < behaviours.Length; index++)
            {
                behaviours[index] = new PlainBehaviour();
            }

            Assert.Throws<ArgumentException>(() => EntityBehaviour.Attach(new ListEntity(behaviours)));
        }

        [Test]
        public void AttachRefusesAMissingBehaviourWithoutAttachingAny()
        {
            var second = new PlainBehaviour();
            var behaviours = new EntityBehaviour[] { new PlainBehaviour(), second, null };

            Assert.Throws<ArgumentException>(() => EntityBehaviour.Attach(new ListEntity(behaviours)));
            Assert.AreEqual(0, second.BehaviourIndex);
            Assert.Throws<ArgumentNullException>(() => EntityBehaviour.Attach(null));
        }

        private sealed class PlainBehaviour : EntityBehaviour
        {
        }

        private sealed class ListEntity : INetworkEntity
        {
            public ListEntity(IReadOnlyList<EntityBehaviour> behaviours)
            {
                EntityBehaviours = behaviours;
            }

            public uint PrefabId => 1;

            public ulong SceneObjectId => 0;

            public bool DespawnWithOwner => true;

            public NetworkVisibility Visibility => NetworkVisibility.Rule;

            public IReadOnlyList<EntityBehaviour> EntityBehaviours { get; }

            public EntityRecord Record => null;

            public Vector3 ReadWorldPosition() => Vector3.Zero;

            public void ReadRootPose(out Vector3 worldPosition, out Quaternion worldRotation, out Vector3 localScale)
            {
                worldPosition = Vector3.Zero;
                worldRotation = Quaternion.Identity;
                localScale = Vector3.One;
            }

            public void Bind(EntityRecord record)
            {
            }

            public void Unbind(EntityRecord record)
            {
            }
        }
    }
}
