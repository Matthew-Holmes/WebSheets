namespace SyntheticPDFs.Git
{
    public partial class GitRepoManager
    {
        private void PrepareRepository()
        {
            // 1. Remove existing repo directory if it exists
            // if this gives a permission error - kill WSL
            var cleanup = BashRunner.RunAsync(
                $"if [ -d \"{_repoDir}\" ]; then rm -rf \"{_repoDir}\"; fi").Result;

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

            // 4. Configure git user.name (repo-local)
            var configName = BashRunner.RunAsync(
                "git config user.name \"Server\"",
                workingDirectory: _repoDir
            ).Result;

            if (!configName.Success)
            {
                LogFailure("Failed to set git user.name", configName);
                throw new InvalidOperationException("git config user.name failed");
            }

            // 5. Configure git user.email (repo-local)
            var configEmail = BashRunner.RunAsync(
                "git config user.email '<>'",
                workingDirectory: _repoDir
            ).Result;


            if (!configEmail.Success)
            {
                LogFailure("Failed to set git user.email", configEmail);
                throw new InvalidOperationException("git config user.email failed");
            }



            _logger.LogInformation(
                "Git repository ready. Source directory: {SourceDir}",
                _sourceDir
            );
        }

    }
}
