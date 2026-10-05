using System.Collections.Generic;
using System;
using Fomoxa.Networking;
using Fomoxa.Unity.Editor;
using NUnit.Framework;

namespace Fomoxa.Unity.Tests
{
    public sealed class FomoxaChannelScannerTest
    {
        [Network]
        [Codec("net", "fast")]
        [NetworkChannel("net", Channel.ReliableOrdered)]
        [NetworkChannel("fast", Channel.Unreliable)]
        private sealed class ReliableOrder
        {
        }

        [Network]
        [Codec("net")]
        private sealed class Position
        {
        }

        [Network]
        [Codec("net")]
        [NetworkChannel("other", Channel.ReliableOrdered)]
        private sealed class UnknownCodec
        {
        }

        [NetworkChannel("net", Channel.ReliableOrdered)]
        private sealed class NotAModel
        {
        }

        [Network]
        [Codec("net")]
        [NetworkChannel("net", Channel.ReliableOrdered)]
        [NetworkChannel("net", Channel.Unreliable)]
        private sealed class TwoChannels
        {
        }

        [Test]
        public void ReliableDeclarationsAreReportedByModelNameAndCodec()
        {
            IReadOnlyList<ChannelDeclaration> declarations = FomoxaChannelScanner.Scan(new[] { typeof(ReliableOrder), typeof(Position) }, out string error);

            Assert.IsNull(error);
            Assert.AreEqual(1, declarations.Count);
            Assert.AreEqual("ReliableOrder", declarations[0].Model);
            Assert.AreEqual("net", declarations[0].Codec);
            Assert.AreEqual(Channel.ReliableOrdered, declarations[0].Channel);
        }

        [Test]
        public void ChannelForACodecTheModelDoesNotDeclareIsAnError()
        {
            Assert.IsNull(FomoxaChannelScanner.Scan(new[] { typeof(UnknownCodec) }, out string error));
            StringAssert.Contains("codec \"other\"", error);
        }

        [Test]
        public void ChannelOnATypeThatIsNotAModelIsAnError()
        {
            Assert.IsNull(FomoxaChannelScanner.Scan(new[] { typeof(NotAModel) }, out string error));
            StringAssert.Contains("is not a [Network] model", error);
        }

        [Test]
        public void TwoChannelsForOneCodecAreAnError()
        {
            Assert.IsNull(FomoxaChannelScanner.Scan(new[] { typeof(TwoChannels) }, out string error));
            StringAssert.Contains("more than one network channel", error);
        }

        [Test]
        public void TheEditorScansOnlyTheModelsFolderAndThePackageNotOtherAssemblies()
        {
            IReadOnlyList<Type> types = FomoxaChannelScanner.TypesForThisEditor("Assets/NoSuchFolder");

            CollectionAssert.DoesNotContain(types, typeof(ReliableOrder));
            CollectionAssert.DoesNotContain(types, typeof(NotAModel));
        }
    }
}
