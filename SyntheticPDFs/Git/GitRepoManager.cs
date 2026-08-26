using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Git
{
    public partial class GitRepoManager : IGitRepoManager
    {
        String _repoUrl;
        String _repoDir;
        String _sourceDir;

        private readonly ContentRepositoryOptions _options;


        public String RepoDir => _repoDir;

        public String SourceDir => _sourceDir;


        private readonly ILogger<GitRepoManager> _logger;

        public GitRepoManager(
            ILogger<GitRepoManager> logger,
            IOptions<ContentRepositoryOptions> options)
        {
            _logger = logger;

            _options = options.Value;

            _repoUrl = _options.CloneUrl;

            _repoDir = _options.LocalDirectory;
            _sourceDir = _options.SourceDirectory;

            PrepareRepository();

            //RepoModel model = GetLatestModelOfRepo();
        }

        #region utilities used throughout

        private void VerifyInGitRepo()
        {
            var verifyRepo = BashRunner.RunAsync(
                "git rev-parse --is-inside-work-tree",
                _logger,
                workingDirectory: _repoDir
            ).Result;

            if (!verifyRepo.Success)
            {
                LogFailure("Not inside a git repository", verifyRepo);
                throw new InvalidOperationException("Not inside a git repository");
            }
        }

        // used by ValidateGitHash - the log parser keeps its own copy
        private static readonly Regex GitFullHashRegex =
            new Regex("^[0-9a-f]{40}$", RegexOptions.Compiled);

        // -i plus IdentitiesOnly does the job ssh-agent used to, without leaving a daemon
        // behind whose cwd is inside the repo - those blocked the startup cleanup.
        // BatchMode stops ssh waiting on a prompt nobody is there to answer
        private static String SshCommand(String keyLoc) =>
            $"GIT_SSH_COMMAND='ssh -i {keyLoc} -o IdentitiesOnly=yes -o BatchMode=yes " +
            "-o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null'";


        private void LogFailure(string message, BashRunner.BashResult result)
        {
            _logger.LogCritical(message);
            _logger.LogCritical("\t stdout: {StdOut}", result.StdOut);
            _logger.LogCritical("\t stderr: {StdErr}", result.StdErr);
        }

        #endregion
    }
}
