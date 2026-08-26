using Agents;
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
                        _logger,
                        workingDirectory: RepoDir
                    ).Result;

                    if (!remove.Success)
                    {
                        LogFailure("git rm failed", remove);
                        throw new InvalidOperationException("git rm failed");
                    }
                }

                String commitMessage = $"removed stale files: {String.Join(" ", filenames)}";

                bool good = await CommitAndPush(commitMessage, "");

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

        public async Task<bool> CommitAndPushTexSource(List<TexSourceModel> texSources, String hash)
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

                foreach (TexSourceModel texSource in texSources)
                {

                    // 2. Ensure directory exists
                    Directory.CreateDirectory(RepoDir + "/" + texSource.DirNoFileName);

                    // 3. Write TeX source to file
                    File.WriteAllText(RepoDir + "/" + texSource.FileNameFullPath, texSource.TexSource);


                    // 4. git add file
                    var add = BashRunner.RunAsync(
                        $"git add \"{texSource.FileNameFullPath}\"",
                        _logger,
                        workingDirectory: RepoDir
                    ).Result;


                    if (!add.Success)
                    {
                        LogFailure("git add failed", add);
                        throw new InvalidOperationException("git add failed");
                    }
                }

                String added = String.Join(" ", texSources.Select(ts => $"{ts.FileNameFullPath.Split('/').Last()}"));

                String description = String.Join(" ", texSources.Select(ts => $"{ts.FileNameFullPath}"));

                var commitMessage = $"Update/Add {added}";

                bool successfulCommit = await CommitAndPush(commitMessage, description);

                if (!successfulCommit) { return false; }

                _logger.LogInformation(
                    $"Committed and pushed TeX source: {added}"
                );

                return true;
            }
            catch (Exception ex)
            {
                Reset(ex, latestHash);
                return false;
            }
        }


        public async Task<bool> CommitAndPushTexSource(TexSourceModel texSource, String hash)
        {
            return await CommitAndPushTexSource(new List<TexSourceModel> { texSource }, hash);
        }

        public void Reset(Exception ex, String? latestHash)
        {
            _logger.LogCritical(ex, "Failure during git action Attempting hard reset...");

            if (!string.IsNullOrEmpty(latestHash))
            {
                var reset = BashRunner.RunAsync(
                    $"git reset --hard {latestHash}",
                    _logger,
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

        public async Task<bool> CommitAndPush(String commitMessage, String? description = null)
        {

            String bash = description is not null ?
                $"git commit -m \"{commitMessage}\" -m \"{description}\""
            :   $"git commit -m \"{commitMessage}\"";


            var commit = BashRunner.RunAsync(
                bash,
                _logger,
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

            int pushTimeoutSeconds = 50;

            String keyLoc = _options.SshKeyPath;

            // BashRunner does the timing out now, inside the distro, so a hung push
            // gets signalled rather than abandoned still running
            var pushResult = await BashRunner.RunAsync(
                $"git remote set-url origin {_options.PushUrl} && " +
                $"{SshCommand(keyLoc)} git push",
                _logger,
                workingDirectory: RepoDir,
                killAfterSeconds: pushTimeoutSeconds
            );

            _logger.LogInformation("Git push completed:");
            _logger.LogInformation("stdout:\n" + pushResult.StdOut);
            _logger.LogInformation("stderr:\n" + pushResult.StdErr);

            if (pushResult.TimedOut)
            {
                LogFailure("git push timed out", pushResult);
                throw new TimeoutException("Git push timed out after " + pushTimeoutSeconds + "s");
            }

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
                _logger,
                workingDirectory: RepoDir
            ).Result;

            if (!verifyRepo.Success)
            {
                LogFailure("Not inside a git repository", verifyRepo);
                throw new InvalidOperationException("Not inside a git repository");
            }

            String keyLoc = _options.SshKeyPath;

            // pull the latest, use the token as this will sometimes fail
            var pull = BashRunner.RunAsync(
                $"git remote set-url origin {_options.PushUrl} && " +
                $"{SshCommand(keyLoc)} git pull",
                _logger,
                workingDirectory: RepoDir,
                killAfterSeconds: 50
            ).Result;


            if (!pull.Success)
            {
                LogFailure("git pull failed", pull);
                throw new InvalidOperationException("git pull failed");
            }

            // 3. Get current HEAD hash
            var hash = BashRunner.RunAsync(
                "git rev-parse HEAD",
                _logger,
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
