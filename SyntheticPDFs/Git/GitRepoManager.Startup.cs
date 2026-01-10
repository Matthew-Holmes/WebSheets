using SyntheticPDFs.Models;

namespace SyntheticPDFs.Git
{
    public partial class GitRepoManager
    {

        private void PrepareRepository()
        {
            // 1. Remove existing repo directory if it exists
            // if this gives a permission error - kill WSL
            var cleanup = BashRunner.RunAsync(
                $"if [ -d \"{RepoDir}\" ]; then rm -rf \"{RepoDir}\"; fi").Result;

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

            // 3. Verify repo directory exists
            var verifyDir = BashRunner.RunAsync(
                $"test -d \"{RepoDir}\""
            ).Result;

            if (!verifyDir.Success)
            {
                _logger.LogCritical($"Expected repo directory does not exist after clone: {RepoDir}");
                throw new DirectoryNotFoundException(_repoDir);
            }

            // 4. Configure git user.name (repo-local)
            var configName = BashRunner.RunAsync(
                "git config user.name \"Server\"",
                workingDirectory: RepoDir
            ).Result;

            if (!configName.Success)
            {
                LogFailure("Failed to set git user.name", configName);
                throw new InvalidOperationException("git config user.name failed");
            }

            // 5. Configure git user.email (repo-local)
            var configEmail = BashRunner.RunAsync(
                "git config user.email '<>'",
                workingDirectory: RepoDir
            ).Result;


            if (!configEmail.Success)
            {
                LogFailure("Failed to set git user.email", configEmail);
                throw new InvalidOperationException("git config user.email failed");
            }


            _logger.LogInformation($"Git repository ready at {RepoDir}");
        }

    }
}
