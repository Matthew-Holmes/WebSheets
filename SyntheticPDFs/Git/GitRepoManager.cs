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

            BashRunner.BashResult freshStart = BashRunner.RunAsync($"rm -r {_repoDir}").Result;

            if (!freshStart.Success)
            {
                _logger.LogCritical("failed to clear up old repo files, cannot continue");
                _logger.LogCritical($"\t stdout: {freshStart.StdOut}\n\t stderr: {freshStart.StdErr}");
            }
        }
    }
}
