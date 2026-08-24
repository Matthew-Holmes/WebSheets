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


            return RepoLogParser.Parse(
                File.ReadAllText(_repoDir + "/" + TransferFileLog),
                File.ReadAllText(_repoDir + "/" + TransferFileLive),
                hash,
                SourceDir);
        }
    }
}
