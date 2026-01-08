using SyntheticPDFs.Logic;
using static System.Net.WebRequestMethods;

namespace SyntheticPDFs.Git
{
    public class GitRepoManager
    {
        String _repoUrl;
        String _sourceDir;
        String _repoDir;


        private readonly ILogger<GitRepoManager> _logger;

        public GitRepoManager(
            ILogger<GitRepoManager> logger,
            String repoUrl = "https://github.com/Matthew-Holmes/Matthews_Mathematics",
            String sourceDir = "Matthews_Mathematics/latex/")
        {
            _logger = logger;

            _repoUrl = repoUrl; _sourceDir = sourceDir;

            _repoDir = _sourceDir.Split("/").First();

            PrepareRepository();
        }

        private void PrepareRepository()
        {
            // 1. Remove existing repo directory if it exists
            var cleanup = BashRunner.RunAsync(
                $"if [ -d \"{_repoDir}\" ]; then rm -rf \"{_repoDir}\"; fi"
            ).Result;

            if (!cleanup.Success)
            {
                LogFailure("Failed to remove existing repository directory", cleanup);
                throw new InvalidOperationException("Repository cleanup failed");
            }

            // 2. Clone repository
            var clone = BashRunner.RunAsync(
                $"git clone \"{_repoUrl}\""
            ).Result;

            if (!clone.Success)
            {
                LogFailure("Git clone failed", clone);
                throw new InvalidOperationException("Git clone failed");
            }

            // 3. Verify source directory exists
            var verifyDir = BashRunner.RunAsync(
                $"test -d \"{_sourceDir}\""
            ).Result;

            if (!verifyDir.Success)
            {
                _logger.LogCritical(
                    "Expected source directory does not exist after clone: {SourceDir}",
                    _sourceDir
                );
                throw new DirectoryNotFoundException(_sourceDir);
            }

            _logger.LogInformation(
                "Git repository ready. Source directory: {SourceDir}",
                _sourceDir
            );
        }

        private void LogFailure(string message, BashRunner.BashResult result)
        {
            _logger.LogCritical(message);
            _logger.LogCritical("\t stdout: {StdOut}", result.StdOut);
            _logger.LogCritical("\t stderr: {StdErr}", result.StdErr);
        }


    }
}
