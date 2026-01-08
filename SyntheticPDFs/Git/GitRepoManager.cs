using Microsoft.Extensions.Logging;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Git
{
    public partial class GitRepoManager
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



        private void LogFailure(string message, BashRunner.BashResult result)
        {
            _logger.LogCritical(message);
            _logger.LogCritical("\t stdout: {StdOut}", result.StdOut);
            _logger.LogCritical("\t stderr: {StdErr}", result.StdErr);
        }
    }
}
