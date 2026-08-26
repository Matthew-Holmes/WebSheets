using System.Diagnostics;
using System.Text;
using System.Threading;

namespace SyntheticPDFs.Git
{
    public static class BashRunner
    {
        public sealed class BashResult
        {
            public int ExitCode { get; init; }
            public string StdOut { get; init; } = string.Empty;
            public string StdErr { get; init; } = string.Empty;
            public bool Success => ExitCode == 0;

            // the exit codes GNU timeout uses when it had to signal the command
            public bool TimedOut => ExitCode == 124 || ExitCode == 137;
        }

        public static async Task<BashResult> RunAsync(
        string command,
        ILogger logger,
        string? workingDirectory = null,
        IDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default,
        int killAfterSeconds = 60)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            // Total permitted time ends up being grace + killAfter
            int hardKillGracePeriod = 5;

            var startInfo = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // opened so it can be closed straight away - anything that tries to
                // prompt then sees EOF instead of blocking forever
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
            };

            if (OperatingSystem.IsWindows())
            {
                startInfo.FileName = "wsl.exe";
                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add("timeout");
            }
            else
            {
                startInfo.FileName = "timeout";
            }

            // timeout runs inside the distro, not on the windows side - wsl.exe is only
            // a relay, so killing it leaves the linux processes running. it also puts the
            // command in its own process group and signals the group, taking ssh with it
            startInfo.ArgumentList.Add("--signal=SIGTERM");
            startInfo.ArgumentList.Add($"--kill-after={hardKillGracePeriod}s");
            startInfo.ArgumentList.Add($"{killAfterSeconds}s");
            startInfo.ArgumentList.Add("bash");
            startInfo.ArgumentList.Add("-c");
            // ArgumentList quotes each element for us, so the script survives untouched
            startInfo.ArgumentList.Add(BuildScript(command, environmentVariables));

            using var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                stdout.AppendLine(e.Data);
                Console.Out.WriteLine(e.Data); // live terminal output
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                stderr.AppendLine(e.Data);
                Console.Error.WriteLine(e.Data); // live terminal error output
            };

            process.Start();

            process.StandardInput.Close();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogCritical($"processed failed!, {e.Message}");
                KillIfRunning(process, logger);
                throw;
            }

            var result = new BashResult
            {
                ExitCode = process.ExitCode,
                StdOut = stdout.ToString(),
                StdErr = stderr.ToString()
            };

            if (result.TimedOut)
            {
                logger.LogWarning($"command timed out after {killAfterSeconds}s: {command}");
            }

            return result;
        }

        // windows environment variables do not cross into WSL unless WSLENV says so,
        // so anything we want set has to be exported by the script itself
        private static string BuildScript(
            string command,
            IDictionary<string, string>? environmentVariables)
        {
            var script = new StringBuilder();

            // without these git or ssh can sit on a passphrase or host key prompt forever
            script.Append("export GIT_TERMINAL_PROMPT=0; unset GIT_ASKPASS SSH_ASKPASS; ");

            if (environmentVariables != null)
            {
                foreach (var kvp in environmentVariables)
                {
                    script.Append($"export {kvp.Key}={ShellQuote(kvp.Value)}; ");
                }
            }

            script.Append(command);

            return script.ToString();
        }

        // close the quote, add an escaped one, reopen - the usual bash idiom
        private static string ShellQuote(string value)
            => "'" + value.Replace("'", @"'""'""'") + "'";

        // only reaps the windows side, the linux children are out of reach from here -
        // that is what the timeout wrapper is for
        private static void KillIfRunning(Process process, ILogger logger)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception e)
            {
                logger.LogWarning($"could not kill process: {e.Message}");
            }
        }
    }
}
