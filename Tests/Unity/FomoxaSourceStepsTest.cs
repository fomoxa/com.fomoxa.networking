using System;
using System.Collections.Generic;
using Fomoxa.Unity.Editor;
using NUnit.Framework;

namespace Fomoxa.Unity.Tests
{
    public sealed class FomoxaSourceStepsTest
    {
        private sealed class FixedStep : IFomoxaSourceStep
        {
            private readonly FomoxaSourceResult result;

            public FixedStep(FomoxaSourceResult result)
            {
                this.result = result;
            }

            public List<FomoxaCompilation> Seen { get; } = new List<FomoxaCompilation>();

            public FomoxaSourceResult Prepare(FomoxaCompilation compilation)
            {
                Seen.Add(compilation);
                return result;
            }
        }

        private static readonly FomoxaCompilation Failed =
            new FomoxaCompilation("/project", "Assets/Generated", true, new[] { "Assets/A.cs(1,1): error CS0001: broken" });

        [Test]
        public void EveryStepSeesTheCompilationAndTheFoldersJoinInOrder()
        {
            var first = new FixedStep(FomoxaSourceResult.Success(new[] { "A", "B" }, "first warning"));
            var second = new FixedStep(FomoxaSourceResult.Success(new[] { "B", "C" }, "second warning"));

            FomoxaSourceResult result = FomoxaSourceSteps.Prepare(new[] { first, second }, Failed);

            Assert.IsTrue(result.Succeeded, result.Error);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, result.SourceFolders);
            Assert.AreEqual("first warning\nsecond warning", result.Warnings);
            Assert.AreSame(Failed, first.Seen[0]);
            Assert.AreSame(Failed, second.Seen[0]);
        }

        [Test]
        public void TheFirstFailureStopsTheSteps()
        {
            var failing = new FixedStep(FomoxaSourceResult.Failure("cannot prepare"));
            var later = new FixedStep(FomoxaSourceResult.Success(new[] { "C" }, null));

            FomoxaSourceResult result = FomoxaSourceSteps.Prepare(new[] { failing, later }, Failed);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("cannot prepare", result.Error);
            Assert.IsEmpty(result.SourceFolders);
            Assert.IsEmpty(later.Seen);
        }

        [Test]
        public void NoStepsAddNoFolders()
        {
            FomoxaSourceResult result = FomoxaSourceSteps.Prepare(Array.Empty<IFomoxaSourceStep>(), Failed);

            Assert.IsTrue(result.Succeeded);
            Assert.IsEmpty(result.SourceFolders);
            Assert.IsNull(result.Warnings);
        }

        [Test]
        public void RegisteringNoStepThrows()
        {
            Assert.Throws<ArgumentNullException>(() => FomoxaSourceSteps.Register(null));
        }
    }
}
