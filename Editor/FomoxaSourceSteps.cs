using System;
using System.Collections.Generic;

namespace Fomoxa.Unity.Editor
{
    public sealed class FomoxaCompilation
    {
        public FomoxaCompilation(string projectRoot, string generatedFolder, bool failed, IReadOnlyList<string> errors)
        {
            ProjectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
            GeneratedFolder = generatedFolder ?? throw new ArgumentNullException(nameof(generatedFolder));
            Failed = failed;
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        public string ProjectRoot { get; }

        public string GeneratedFolder { get; }

        public bool Failed { get; }

        public IReadOnlyList<string> Errors { get; }
    }

    public readonly struct FomoxaSourceResult
    {
        private FomoxaSourceResult(IReadOnlyList<string> sourceFolders, string error, string warnings)
        {
            SourceFolders = sourceFolders;
            Error = error;
            Warnings = warnings;
        }

        public bool Succeeded => Error == null;

        public IReadOnlyList<string> SourceFolders { get; }

        public string Error { get; }

        public string Warnings { get; }

        public static FomoxaSourceResult Success(IReadOnlyList<string> sourceFolders, string warnings) =>
            new FomoxaSourceResult(sourceFolders ?? throw new ArgumentNullException(nameof(sourceFolders)), null, warnings);

        public static FomoxaSourceResult Failure(string error) =>
            new FomoxaSourceResult(Array.Empty<string>(), error ?? throw new ArgumentNullException(nameof(error)), null);
    }

    public interface IFomoxaSourceStep
    {
        FomoxaSourceResult Prepare(FomoxaCompilation compilation);
    }

    public static class FomoxaSourceSteps
    {
        private static readonly List<IFomoxaSourceStep> registered = new List<IFomoxaSourceStep>();

        public static void Register(IFomoxaSourceStep step)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            if (!registered.Contains(step))
            {
                registered.Add(step);
            }
        }

        public static FomoxaSourceResult Prepare(FomoxaCompilation compilation) => Prepare(registered, compilation);

        internal static FomoxaSourceResult Prepare(IEnumerable<IFomoxaSourceStep> steps, FomoxaCompilation compilation)
        {
            var folders = new List<string>();
            string warnings = null;
            foreach (IFomoxaSourceStep step in steps)
            {
                FomoxaSourceResult result = step.Prepare(compilation);
                if (!result.Succeeded)
                {
                    return result;
                }

                foreach (string folder in result.SourceFolders)
                {
                    if (!folders.Contains(folder))
                    {
                        folders.Add(folder);
                    }
                }

                if (result.Warnings != null)
                {
                    warnings = warnings == null ? result.Warnings : warnings + "\n" + result.Warnings;
                }
            }

            return FomoxaSourceResult.Success(folders, warnings);
        }
    }
}
