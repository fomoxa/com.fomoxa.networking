using System;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class RpcMessageIdsTest
    {
        private class Base
        {
        }

        private sealed class Derived : Base
        {
        }

        private sealed class Other
        {
        }

        [Test]
        public void IdIsFoundForTheDeclaringTypeAndItsSubclasses()
        {
            var ids = new RpcMessageIds();
            ids.Set(typeof(Base).FullName, "Jump", 0x11);

            Assert.IsTrue(ids.TryGet(typeof(Base), "Jump", out uint onBase));
            Assert.IsTrue(ids.TryGet(typeof(Derived), "Jump", out uint onDerived));
            Assert.AreEqual(0x11u, onBase);
            Assert.AreEqual(0x11u, onDerived);
            Assert.IsFalse(ids.TryGet(typeof(Other), "Jump", out _));
            Assert.IsFalse(ids.TryGet(typeof(Base), "Fire", out _));
            Assert.AreEqual(1, ids.Count);
        }

        [Test]
        public void TheMostDerivedDeclarationWins()
        {
            var ids = new RpcMessageIds();
            ids.Set(typeof(Base).FullName, "Jump", 0x11);
            ids.Set(typeof(Derived).FullName, "Jump", 0x22);

            Assert.IsTrue(ids.TryGet(typeof(Derived), "Jump", out uint onDerived));
            Assert.IsTrue(ids.TryGet(typeof(Base), "Jump", out uint onBase));
            Assert.AreEqual(0x22u, onDerived);
            Assert.AreEqual(0x11u, onBase);
        }

        [Test]
        public void SetAfterALookupReplacesTheResolvedId()
        {
            var ids = new RpcMessageIds();
            ids.Set(typeof(Base).FullName, "Jump", 0x11);
            ids.TryGet(typeof(Derived), "Jump", out _);

            ids.Set(typeof(Derived).FullName, "Jump", 0x22);

            Assert.IsTrue(ids.TryGet(typeof(Derived), "Jump", out uint onDerived));
            Assert.AreEqual(0x22u, onDerived);
        }

        [Test]
        public void NullArgumentsThrow()
        {
            var ids = new RpcMessageIds();

            Assert.Throws<ArgumentNullException>(() => ids.Set(null, "Jump", 1));
            Assert.Throws<ArgumentNullException>(() => ids.Set("Type", null, 1));
            Assert.Throws<ArgumentNullException>(() => ids.TryGet(null, "Jump", out _));
            Assert.Throws<ArgumentNullException>(() => ids.TryGet(typeof(Base), null, out _));
        }
    }
}
