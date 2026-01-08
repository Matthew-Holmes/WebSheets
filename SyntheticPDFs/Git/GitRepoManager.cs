using Microsoft.Extensions.Logging;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Git
{
    public class GitRepoManager
    {
        String _repoUrl;
        String _sourceDir;
        String _repoDir;

        public String SourceDir => _sourceDir;

        public String RepoDir => _repoDir;


        private readonly ILogger<GitRepoManager> _logger;

        public GitRepoManager(
            ILogger<GitRepoManager> logger,
            String repoUrl = "https://github.com/Matthew-Holmes/Matthews_Mathematics",
            String sourceDir = "Matthews_Mathematics/latex")
        {
            _logger = logger;

            _repoUrl = repoUrl; _sourceDir = sourceDir;

            _repoDir = _sourceDir.Split("/").First();

            PrepareRepository();

        }


        public async Task<bool> CommitAndPushTexSource(TexSourceModel texSource)
        {
            string latestHash = null!;
            try
            {
                // 1. Pull the latest changes and get the hash
                latestHash = PullLatestAndGetHash();
                _logger.LogInformation($"Repo is at commit: {latestHash}");

                // 2. Ensure directory exists
                Directory.CreateDirectory(texSource.DirNoFileName);

                // 3. Write TeX source to file
                File.WriteAllText(texSource.FileNameFullPath, texSource.TexSource);

                // 4. Verify repo is a git repo
                var verifyRepo = BashRunner.RunAsync(
                    "git rev-parse --is-inside-work-tree",
                    workingDirectory: _repoDir
                ).Result;

                if (!verifyRepo.Success)
                {
                    LogFailure("Not inside a git repository", verifyRepo);
                    throw new InvalidOperationException("Not inside a git repository");
                }

                // because we need to run this in the git dir, remove the working directory
                // components from the file path

                List<String> repoPath = RepoDir.Split("/").ToList();
                List<String> filePath = texSource.FileNameFullPath.Split("/").ToList();

                while (repoPath.Count != 0)
                {
                    if (repoPath[0] != filePath[0])
                    {
                        throw new ArgumentException("tex source must be in the repository");
                    }

                    repoPath.RemoveAt(0);
                    filePath.RemoveAt(0);
                }

                // Recombine the remaining filePath to get the relative path inside the repo
                string relativePath = string.Join("/", filePath);

                // 5. git add file
                var add = BashRunner.RunAsync(
                    $"git add \"{relativePath}\"",
                    workingDirectory: _repoDir
                ).Result;

                if (!add.Success)
                {
                    LogFailure("git add failed", add);
                    throw new InvalidOperationException("git add failed");
                }

                // 6. git commit
                var commitMessage = $"Update {texSource.FileNameNoPathNoExt}.tex";
                var commit = BashRunner.RunAsync(
                    $"git commit -m \"{commitMessage}\"",
                    workingDirectory: _repoDir
                ).Result;

                // No changes to commit is OK
                if (!commit.Success)
                {
                    if (commit.StdErr.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("No changes to commit for {File}", texSource.FileNameFullPath);
                        return true;
                    }

                    LogFailure("git commit failed", commit);
                    throw new InvalidOperationException("git commit failed");
                }

                int? timeoutMs = 50000; // 5 sec timeout
                CancellationTokenSource cts = new CancellationTokenSource();

                String keyLoc = OperatingSystem.IsWindows() ? "/home/matt/root/.ssh/id_ed25519" : "/root/.ssh/id_ed25519";

                var pushTask = BashRunner.RunAsync(
                    $"eval $(ssh-agent -s) && ssh-add {keyLoc} && " +
                    "git remote set-url origin git@github.com:Matthew-Holmes/Matthews_Mathematics.git && " +
                    "GIT_SSH_COMMAND='ssh -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null' git push",
                    workingDirectory: _repoDir,
                    cancellationToken: cts.Token
                );


                if (timeoutMs.HasValue)
                {
                    var completedTask = await Task.WhenAny(pushTask, Task.Delay(timeoutMs.Value));

                    if (completedTask != pushTask)
                    {
                        // Timeout reached
                        var partialResult = pushTask.IsCompletedSuccessfully ? pushTask.Result : null;

                        if (partialResult != null)
                        {
                            _logger.LogWarning("Git push timed out. Partial output:");
                            _logger.LogWarning("\t stdout:\n" + partialResult.StdOut);
                            _logger.LogWarning("\t stderr:\n" + partialResult.StdErr);
                        }
                        else
                        {
                            _logger.LogWarning("Git push timed out. No output available yet.");
                        }

                        _logger.LogInformation("cancelling task");
                        cts.Cancel();

                        throw new TimeoutException("Git push timed out after " + timeoutMs.Value + "ms");
                    }
                }

                // Await the result if it completed on time
                var pushResult = await pushTask;

                // Optionally log output
                _logger.LogInformation("Git push completed:");
                _logger.LogInformation("stdout:\n" + pushResult.StdOut);
                _logger.LogInformation("stderr:\n" + pushResult.StdErr);

                if (!pushResult.Success)
                {
                    LogFailure("git push failed", pushResult);
                    throw new InvalidOperationException("git push failed");
                }

                _logger.LogInformation(
                    "Committed and pushed TeX source: {File}",
                    texSource.FileNameFullPath
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failure during commit/push. Attempting hard reset...");

                if (!string.IsNullOrEmpty(latestHash))
                {
                    var reset = BashRunner.RunAsync(
                        $"git reset --hard {latestHash}",
                        workingDirectory: _repoDir
                    ).Result;

                    if (reset.Success)
                    {
                        _logger.LogInformation("Repository reset back to commit: {Hash}", latestHash);
                    }
                    else
                    {
                        LogFailure($"Failed to reset repo back to {latestHash}", reset);
                    }
                }

                return false;
            }
        }

        public String PullLatestAndGetHash()
        {
            // 1. Ensure we are in a git repo
            var verifyRepo = BashRunner.RunAsync(
                "git rev-parse --is-inside-work-tree",
                workingDirectory: _repoDir
            ).Result;

            if (!verifyRepo.Success)
            {
                LogFailure("Not inside a git repository", verifyRepo);
                throw new InvalidOperationException("Not inside a git repository");
            }

            // 2. Pull latest changes
            var pull = BashRunner.RunAsync(
                "git pull",
                workingDirectory: _repoDir
            ).Result;

            if (!pull.Success)
            {
                LogFailure("git pull failed", pull);
                throw new InvalidOperationException("git pull failed");
            }

            // 3. Get current HEAD hash
            var hash = BashRunner.RunAsync(
                "git rev-parse HEAD",
                workingDirectory: _repoDir
            ).Result;

            if (!hash.Success)
            {
                LogFailure("Failed to get git hash", hash);
                throw new InvalidOperationException("git rev-parse HEAD failed");
            }

            var commitHash = hash.StdOut.Trim();

            ValidateGitHash(commitHash);

            _logger.LogInformation(
                "Repository updated successfully. Current HEAD: {Hash}",
                commitHash
            );

            return commitHash;
        }




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

        private void LogFailure(string message, BashRunner.BashResult result)
        {
            _logger.LogCritical(message);
            _logger.LogCritical("\t stdout: {StdOut}", result.StdOut);
            _logger.LogCritical("\t stderr: {StdErr}", result.StdErr);
        }

        private static readonly Regex GitFullHashRegex =
            new Regex("^[0-9a-f]{40}$", RegexOptions.Compiled);

        private void ValidateGitHash(string hash)
        {
            if (!GitFullHashRegex.IsMatch(hash))
            {
                _logger.LogCritical(
                    "Invalid git commit hash format: '{Hash}'",
                    hash
                );

                throw new FormatException(
                    $"Invalid git commit hash format: '{hash}'"
                );
            }
        }


    }
}
