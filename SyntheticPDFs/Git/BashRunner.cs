using System.Diagnostics;
using System.Text;

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
        }

        public static async Task<BashResult> RunAsync(
        string command,
        string? workingDirectory = null,
        IDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            var isWindows = OperatingSystem.IsWindows();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = isWindows ? "wsl.exe" : "/bin/bash",
                    Arguments = isWindows
                        ? $"-e bash -c \"{command}\""
                        : $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = false,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
                },
                EnableRaisingEvents = true
            };

            if (environmentVariables != null)
            {
                foreach (var kvp in environmentVariables)
                {
                    process.StartInfo.Environment[kvp.Key] = kvp.Value;
                }
            }

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

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            return new BashResult
            {
                ExitCode = process.ExitCode,
                StdOut = stdout.ToString(),
                StdErr = stderr.ToString()
            };
        }

    }
}
