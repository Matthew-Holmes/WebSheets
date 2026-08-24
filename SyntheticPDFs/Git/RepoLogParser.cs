using SyntheticPDFs.Models;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Git
{
    // pure parsing of git output, split out from GitRepoManager so it can be tested
    // without a constructor that clones a repository
    public static class RepoLogParser
    {
        private static readonly Regex GitFullHashRegex =
            new Regex("^[0-9a-f]{40}$", RegexOptions.Compiled);

        // grabbing all the history and parsing here is faster than the slow git methods
        // assume most files have O(1) git commits, then this is O(N) where N is the repo size
        public static RepoModel Parse(
            String log,
            String liveFilesWithDotSlashPrefixes,
            String hash,
            String sourceDir)
        {
            HashSet<String> live = liveFilesWithDotSlashPrefixes
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 2)
                .Select(l => l[2..] /* drop the ./ */)
                .Where(l => l.StartsWith(sourceDir + '/'))
                .ToHashSet();


            var fileAges = new Dictionary<string, int>(StringComparer.Ordinal);
            var pendingFiles = new List<string>();

            int commitIndex = 0;

            var lines = log
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim());

            foreach (var line in lines)
            {
                if (GitFullHashRegex.IsMatch(line))
                {
                    // reached a commit boundary
                    foreach (var file in pendingFiles)
                    {
                        // only record the most recent edit
                        if (!fileAges.ContainsKey(file))
                        {
                            fileAges[file] = commitIndex;
                        }
                    }

                    pendingFiles.Clear();
                    commitIndex++;
                }
                else if (live.Contains(line))
                {
                    // deleted files still appear in the log, so only keep ones still on disk
                    pendingFiles.Add(line);
                }
            }

            // the final commit's files never hit a boundary, so flush them here
            foreach (var file in pendingFiles)
            {
                if (!fileAges.ContainsKey(file))
                {
                    fileAges[file] = commitIndex;
                }
            }

            var contents = fileAges
                .Select(kvp => new TrackedFile
                {
                    FullPath = kvp.Key,
                    AgeCommits = kvp.Value
                })
                .OrderBy(tf => tf.FullPath)
                .ToList()
                .AsReadOnly();

            return new RepoModel
            {
                Contents = contents,
                LastCommitHash = hash
            };
        }
    }
}
