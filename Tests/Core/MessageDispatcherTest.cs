using System;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class MessageDispatcherTest
    {
        private const uint DeclaredId = 0x1000_0001;
        private const uint UndeclaredId = 0x1000_0002;
        private const uint ObjectCallId = 0x1000_0003;

        private static Schema SchemaWithOneMessage() =>
            new Schema(0xAAAA, new[]
            {
                new MessageSchema(DeclaredId, 0xBBBB, new ulong[] { 0xBBBB }),
                new MessageSchema(ObjectCallId, 0xCCCC, new ulong[] { 0xCCCC }),
            });

        private static byte[] ObjectData(uint objectId, byte behaviourIndex, params byte[] body)
        {
            var data = new byte[ObjectHeader.UnreliableLength + body.Length];
            ObjectHeader.WriteUnreliable(data, objectId, behaviourIndex);
            body.CopyTo(data, ObjectHeader.UnreliableLength);
            return data;
        }

        [Test]
        public void RegisteredHandlerReceivesPeerAndPayload()
        {
            var dispatcher = new MessageDispatcher(SchemaWithOneMessage());
            ulong seenPeer = 0;
            byte[] seenPayload = null;
            dispatcher.Register(DeclaredId, (peerId, payload) =>
            {
                seenPeer = peerId;
                seenPayload = payload.ToArray();
            });

            Assert.IsTrue(dispatcher.Dispatch(7, DeclaredId, new byte[] { 1, 2 }));
            Assert.AreEqual(7, seenPeer);
            Assert.AreEqual(new byte[] { 1, 2 }, seenPayload);
        }

        [Test]
        public void MessageWithoutHandlerIsNotDispatched()
        {
            var dispatcher = new MessageDispatcher(SchemaWithOneMessage());

            Assert.IsFalse(dispatcher.Dispatch(1, DeclaredId, ReadOnlyMemory<byte>.Empty));
        }

        [Test]
        public void SecondHandlerForTheSameIdIsRejected()
        {
            var dispatcher = new MessageDispatcher(SchemaWithOneMessage());
            dispatcher.Register(DeclaredId, (peerId, payload) => { });

            Assert.Throws<HandlerRegistrationException>(() => dispatcher.Register(DeclaredId, (peerId, payload) => { }));
        }

        [Test]
        public void ObjectHeaderIsTheObjectIdLittleEndianThenTheBehaviourIndex()
        {
            var header = new byte[ObjectHeader.UnreliableLength];

            ObjectHeader.WriteUnreliable(header, 0x0102_0304, 7);

            Assert.AreEqual(new byte[] { 0x04, 0x03, 0x02, 0x01, 7 }, header);
        }

        [Test]
        public void ObjectHandlerReceivesTheObjectIdTheBehaviourIndexAndTheBodyAfterTheHeader()
        {
            var dispatcher = new MessageDispatcher(SchemaWithOneMessage());
            ulong seenPeer = 0;
            uint seenObject = 0;
            byte seenBehaviour = 0;
            byte[] seenBody = null;
            dispatcher.RegisterObject(ObjectCallId, (peerId, objectId, behaviourIndex, body) =>
            {
                seenPeer = peerId;
                seenObject = objectId;
                seenBehaviour = behaviourIndex;
                seenBody = body.ToArray();
            });

            Assert.IsTrue(dispatcher.Dispatch(3, ObjectCallId, ObjectData(0xABCD_0001, 2, 9, 8)));
            Assert.AreEqual(3ul, seenPeer);
            Assert.AreEqual(0xABCD_0001u, seenObject);
            Assert.AreEqual(2, seenBehaviour);
            Assert.AreEqual(new byte[] { 9, 8 }, seenBody);
            Assert.IsTrue(dispatcher.IsObjectMessage(ObjectCallId));
            Assert.IsFalse(dispatcher.IsObjectMessage(DeclaredId));
        }

        [Test]
        public void OneIdIsEitherAPlainMessageOrAnObjectMessage()
        {
            var plainFirst = new MessageDispatcher(SchemaWithOneMessage());
            plainFirst.Register(DeclaredId, (peerId, payload) => { });
            var objectFirst = new MessageDispatcher(SchemaWithOneMessage());
            objectFirst.RegisterObject(DeclaredId, (peerId, objectId, behaviourIndex, body) => { });

            Assert.Throws<HandlerRegistrationException>(() => plainFirst.RegisterObject(DeclaredId, (peerId, objectId, behaviourIndex, body) => { }));
            Assert.Throws<HandlerRegistrationException>(() => objectFirst.Register(DeclaredId, (peerId, payload) => { }));
            Assert.Throws<HandlerRegistrationException>(() => objectFirst.RegisterObject(DeclaredId, (peerId, objectId, behaviourIndex, body) => { }));
        }

        [Test]
        public void ObjectHandlerForAnIdOutsideTheSchemaIsRejected()
        {
            var dispatcher = new MessageDispatcher(SchemaWithOneMessage());

            Assert.Throws<HandlerRegistrationException>(() => dispatcher.RegisterObject(UndeclaredId, (peerId, objectId, behaviourIndex, body) => { }));
        }

        [Test]
        public void HandlerForAnIdOutsideTheSchemaIsRejected()
        {
            var dispatcher = new MessageDispatcher(SchemaWithOneMessage());

            Assert.Throws<HandlerRegistrationException>(() => dispatcher.Register(UndeclaredId, (peerId, payload) => { }));
        }
    }
}
