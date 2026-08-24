using System.Collections.ObjectModel;
using SyntheticPDFs.Git;
using SyntheticPDFs.Models;

namespace SyntheticPDFs.Tests.Fakes
{
    // serves an in-memory repo and records what the orchestrator tried to do to it
    public class FakeGitRepoManager : IGitRepoManager
    {
        public const String Hash = "0123456789abcdef0123456789abcdef01234567";

        // path -> age in commits, lower is younger
        public Dictionary<String, int> Files { get; } = new(StringComparer.Ordinal);

        // path -> file contents, for GetContent
        public Dictionary<String, String> Contents { get; } = new(StringComparer.Ordinal);

        // when true both write operations report failure, as they do on a hash mismatch
        public bool SimulateConflict { get; set; }

        public List<List<String>> RemoveFilesCalls { get; } = new();
        public List<List<TexSourceModel>> CommitCalls { get; } = new();

        public List<TexSourceModel> LastCommit =>
            CommitCalls.Count > 0 ? CommitCalls[^1] : new List<TexSourceModel>();

        public void AddFile(String path, int ageCommits, String contents = "\\documentclass{article}")
        {
            Files[path] = ageCommits;
            Contents[path] = contents;
        }

        public RepoModel GetLatestModelOfRepo()
        {
            var tracked = Files
                .Select(kvp => new TrackedFile { FullPath = kvp.Key, AgeCommits = kvp.Value })
                .OrderBy(tf => tf.FullPath)
                .ToList();

            return new RepoModel
            {
                Contents = new ReadOnlyCollection<TrackedFile>(tracked),
                LastCommitHash = Hash,
            };
        }

        public TexSourceModel GetContent(String filename)
        {
            if (!Contents.TryGetValue(filename, out var contents))
            {
                throw new FileNotFoundException($"fake repo has no file {filename}");
            }

            return new TexSourceModel { FileNameFullPath = filename, TexSource = contents };
        }

        public Task<bool> RemoveFiles(List<String> filenames, String hash)
        {
            RemoveFilesCalls.Add(filenames);

            if (SimulateConflict) { return Task.FromResult(false); }

            foreach (var f in filenames)
            {
                Files.Remove(f);
                Contents.Remove(f);
            }

            return Task.FromResult(true);
        }

        public Task<bool> CommitAndPushTexSource(List<TexSourceModel> texSources, String hash)
        {
            CommitCalls.Add(texSources);

            if (SimulateConflict) { return Task.FromResult(false); }

            // everything already present ages by one commit, the new files are youngest
            foreach (var key in Files.Keys.ToList()) { Files[key] += 1; }

            foreach (var ts in texSources)
            {
                Files[ts.FileNameFullPath] = 0;
                Contents[ts.FileNameFullPath] = ts.TexSource;
            }

            return Task.FromResult(true);
        }
    }
}
