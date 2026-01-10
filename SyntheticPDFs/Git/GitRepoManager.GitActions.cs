using SyntheticPDFs.Models;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Git
{
    public partial class GitRepoManager
    {
        public async Task<bool> RemoveFiles(List<String> filenames, String hash)
        {
            string latestHash = null!;
            try
            {
                // 1. Pull the latest changes and get the hash
                latestHash = PullLatestAndGetHash();
                _logger.LogInformation($"Repo is at commit: {latestHash}");

                if (latestHash != hash)
                {
                    _logger.LogWarning("unexpected hash, backing off!");
                    return false;
                }

                foreach (String filename in filenames)
                {
                    var remove = BashRunner.RunAsync(
                        $"git rm {filename}",
                        workingDirectory: RepoDir
                    ).Result;

                    if (!remove.Success)
                    {
                        LogFailure("git rm failed", remove);
                        throw new InvalidOperationException("git rm failed");
                    }
                }

                String commitMessage = $"removed stale files: {String.Join(" ", filenames)}";

                bool good = await CommitAndPush(commitMessage);

                if (!good)
                {
                    _logger.LogWarning("failed to commit and push stale file deletion");
                }
                return good;
            }
            catch (Exception ex)
            {
                Reset(ex, latestHash);
                return false;
            }
        }

        public async Task<bool> CommitAndPushTexSource(TexSourceModel texSource, String hash)
        {
            // TODO - make this not need the Matthew

            string latestHash = null!;
            try
            {
                // 1. Pull the latest changes and get the hash
                latestHash = PullLatestAndGetHash();
                _logger.LogInformation($"Repo is at commit: {latestHash}");

                if (latestHash != hash)
                {
                    _logger.LogWarning("unexpected hash, backing off!");
                    return false;
                }

                // 2. Ensure directory exists
                Directory.CreateDirectory(RepoDir + "/" + texSource.DirNoFileName);

                // 3. Write TeX source to file
                File.WriteAllText(RepoDir + "/" + texSource.FileNameFullPath, texSource.TexSource);

                // 4. Verify repo is a git repo (overkill?)
                //VerifyInGitRepo();

                // 5. git add file
                var add = BashRunner.RunAsync(
                    $"git add \"{texSource.FileNameFullPath}\"",
                    workingDirectory: RepoDir
                ).Result;

                if (!add.Success)
                {
                    LogFailure("git add failed", add);
                    throw new InvalidOperationException("git add failed");
                }

                var commitMessage = $"Update {texSource.FileNameNoPathNoExt}.tex";

                bool successfulCommit = await CommitAndPush(commitMessage);

                if (!successfulCommit) { return false; }

                _logger.LogInformation(
                    "Committed and pushed TeX source: {File}",
                    texSource.FileNameFullPath
                );

                return true;
            }
            catch (Exception ex)
            {
                Reset(ex, latestHash);
                return false;
            }
        }

        public void Reset(Exception ex, String? latestHash)
        {
            _logger.LogCritical(ex, "Failure during git action Attempting hard reset...");

            if (!string.IsNullOrEmpty(latestHash))
            {
                var reset = BashRunner.RunAsync(
                    $"git reset --hard {latestHash}",
                    workingDirectory: RepoDir
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

        }

        public async Task<bool> CommitAndPush(String commitMessage)
        {
            // 6. git commit
            
            var commit = BashRunner.RunAsync(
                $"git commit -m \"{commitMessage}\"",
                workingDirectory: RepoDir
            ).Result;

            // No changes to commit is OK
            if (!commit.Success)
            {
                if (commit.StdErr.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation($"nothing to commit, attempted: \"{commitMessage}\"");
                    return true;
                }

                LogFailure("git commit failed", commit);
                throw new InvalidOperationException("git commit failed");
            }

            int? timeoutMs = 50000; // 50 sec timeout
            CancellationTokenSource cts = new CancellationTokenSource();

            String keyLoc = OperatingSystem.IsWindows() ? "/home/matt/root/.ssh/id_ed25519" : "/root/.ssh/id_ed25519";

            var pushTask = BashRunner.RunAsync(
                $"eval $(ssh-agent -s) && ssh-add {keyLoc} && " +
                "git remote set-url origin git@github.com:Matthew-Holmes/Matthews_Mathematics.git && " +
                "GIT_SSH_COMMAND='ssh -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null' git push",
                workingDirectory: RepoDir,
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


            return true;
        }


        public String PullLatestAndGetHash()
        {
            // 1. Ensure we are in a git repo
            var verifyRepo = BashRunner.RunAsync(
                "git rev-parse --is-inside-work-tree",
                workingDirectory: RepoDir
            ).Result;

            if (!verifyRepo.Success)
            {
                LogFailure("Not inside a git repository", verifyRepo);
                throw new InvalidOperationException("Not inside a git repository");
            }

            // 2. Pull latest changes
            var pull = BashRunner.RunAsync(
                "git pull",
                workingDirectory: RepoDir
            ).Result;

            if (!pull.Success)
            {
                LogFailure("git pull failed", pull);
                throw new InvalidOperationException("git pull failed");
            }

            // 3. Get current HEAD hash
            var hash = BashRunner.RunAsync(
                "git rev-parse HEAD",
                workingDirectory: RepoDir
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
