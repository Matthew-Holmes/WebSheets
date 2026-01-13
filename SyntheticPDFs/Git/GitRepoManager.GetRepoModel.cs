using SyntheticPDFs.Models;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace SyntheticPDFs.Git
{
    public partial class GitRepoManager
    {
        // this is hacky - but when will I ever need files with these names
        private string TransferFileLog => "transferFile_usri9ae584bn952vrplmlwd0hu1p2r.txt";
        private string TransferFileLive => "transferFile_6oqjjurw3dbealw0ef08bw7nrfzpvx.txt";


        public RepoModel GetLatestModelOfRepo()
        {
            String hash = PullLatestAndGetHash();

            _logger.LogInformation($"pulled latest at commit {hash}");

            VerifyInGitRepo();

            File.WriteAllText(RepoDir + "/" + TransferFileLog,  String.Empty); // clear out the file if it has stuff in
            File.WriteAllText(RepoDir + "/" + TransferFileLive, String.Empty); // ""

            String repoDetailsCommand = $"git log --all --oneline --name-only --format=\"%H\" > {TransferFileLog}"; 

            var GetRepoDetails = BashRunner.RunAsync(repoDetailsCommand, _logger, RepoDir).Result;

            // just grabbing all the history and parsing in C# is faster than the slow git methods
            // assume most files have O(1) git commits, then this is O(N) where N is the repo size
            // could cache for speed, then just look at the commits not seen.... but thats not needed now
            // use transfer file to avoid buffer issues with Stdout


            if (!GetRepoDetails.Success)
            {
                LogFailure("failed to get repo details", GetRepoDetails);
                throw new Exception("failed to get repo details");
            }

            // get the files currently in play - deleted stuff may cause problems

            String liveFilesCommand = $"find ./ > {TransferFileLive}";

            var getLiveFilesDetails = BashRunner.RunAsync(liveFilesCommand, _logger, RepoDir).Result;

            if (!getLiveFilesDetails.Success)
            {
                LogFailure("failed to get details of files in play", getLiveFilesDetails);
                throw new Exception("failed to get file details");
            }


            return Parse(
                File.ReadAllText(_repoDir + "/" + TransferFileLog),
                File.ReadAllText(_repoDir + "/" + TransferFileLive),
                hash);
        }

        public RepoModel Parse(String input, String liveFilesWithDotSlashPrefixes, String hash)
        {
            HashSet<String> live = liveFilesWithDotSlashPrefixes
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Select(l => l[2..] /* drop the ./ */)
                .Where(l => l.StartsWith(SourceDir + '/'))
                .ToHashSet();
            
            
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
                    if (!live.Contains(line))
                    {
                        // this isn't a live file - skip it
                        continue;
                    }
                    else
                    {
                        // File path
                        pendingFiles.Add(line);
                    }
                }
            }

            // file ages only contains live files so this will be OK

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
