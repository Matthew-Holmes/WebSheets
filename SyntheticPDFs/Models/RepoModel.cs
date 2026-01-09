using System.Collections.ObjectModel;

namespace SyntheticPDFs.Models
{

    public record class TrackedFile
    {
        public required String FullPath { get; init; }
        public required int AgeCommits { get; init; }
    }

    public record class RepoModel
    {
        public required ReadOnlyCollection<TrackedFile> Contents { get; init; }

        public required String LastCommitHash { get; init; }
    }
}
