using SyntheticPDFs.Models;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace SyntheticPDFs.Git
{
    public partial class GitRepoManager
    {
        private string TransferFile => "transferFile_usri9ae584bn952vrplmlwd0hu1p2r.txt";
        // hacky but will I ever make a worksheet with this name...?

        public RepoModel GetLatestModelOfRepo()
        {
            String hash = PullLatestAndGetHash();

            _logger.LogInformation($"pulled latest at commit {hash}");

            VerifyInGitRepo();

            File.WriteAllText(_repoDir + "/" + TransferFile, String.Empty); // clear out the file if it has stuff in

            String repoDetailsCommand = $"git log --all --oneline --name-only --format=\"%H\" > {TransferFile}"; 
            // TODO - does this definitely avoid pagination issues???

            // just grabbing all the history and parsing in C# is faster than the slow git methods
            // assume most files have O(1) git commits, then this is O(N) where N is the repo size
            // could cache for speed, then just look at the commits not seen.... but thats not needed now
            // use transfer file to avoid buffer issues with Stdout

            var GetRepoDetails = BashRunner.RunAsync(repoDetailsCommand, _repoDir).Result;

            if (!GetRepoDetails.Success)
            {
                LogFailure("failed to get repo details", GetRepoDetails);
                throw new Exception("failed to get repo details");
            }

            return Parse(File.ReadAllText(_repoDir + "/" + TransferFile), hash);
        }

        public static RepoModel Parse(string input, string hash)
        {
            var fileAges = new Dictionary<string, int>(StringComparer.Ordinal);
            var pendingFiles = new List<string>();

            int commitIndex = 0;

            var lines = input
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim());

            foreach (var line in lines)
            {
                if (GitFullHashRegex.IsMatch(line))
                {
                    // We reached a commit boundary
                    foreach (var file in pendingFiles)
                    {
                        // Only record the most recent edit
                        if (!fileAges.ContainsKey(file))
                        {
                            fileAges[file] = commitIndex;
                        }
                    }

                    pendingFiles.Clear();
                    commitIndex++;
                }
                else
                {
                    // File path
                    pendingFiles.Add(line);
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
