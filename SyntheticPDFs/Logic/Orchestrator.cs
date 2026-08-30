using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Git;
using SyntheticPDFs.Services;
using Shared;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        private readonly SemaphoreSlim _lock = new(1, 1);

        private bool _isRunning;
        private bool _isQueued; 

        private IGitRepoManager RepoManager { get; set; }
        private ILLMService LLMService { get; set; }


        private readonly ILogger<Orchestrator> _logger;

        // which languages may be generated, and what each needs to be typeset. optional
        // so that the English pipeline runs with no translation settings at all
        internal LanguageTable Languages { get; init; }

        // colours, which end up recorded in the provenance block of every file built
        // from them
        internal L2Options L2Settings { get; init; }

        // where the repository is and, within it, where the shared dictionary lives
        internal ContentRepositoryOptions ContentRepository { get; init; }

        public Orchestrator(
            ILogger<Orchestrator> logger,
            IGitRepoManager repoManager,
            ILLMService lLMService,
            IOptions<GenerationOptions> options,
            IOptions<L2Options>? l2Options = null,
            IOptions<ContentRepositoryOptions>? contentRepository = null)
        {
            _logger = logger;

            RepoManager = repoManager;
            LLMService = lLMService;

            MaxFilesToGenerateBase = options.Value.MaxFilesPerRun;
            MaxFilesToGenerate = options.Value.MaxFilesPerRun;

            L2Settings = l2Options?.Value ?? new L2Options();

            ContentRepository = contentRepository?.Value ?? new ContentRepositoryOptions();

            Languages = new LanguageTable(L2Settings, logger);
        }

        public PingResult Ping()
        {
            _lock.Wait();
            try
            {
                if (!_isRunning)
                {
                    _isRunning = true;
                    StartTask();

                    return new PingResult(
                        PingOutcome.Started,
                        IsRunning: true,
                        IsQueued: false
                    );
                }

                if (!_isQueued)
                {
                    _isQueued = true;

                    return new PingResult(
                        PingOutcome.Queued,
                        IsRunning: true,
                        IsQueued: true
                    );
                }

                return new PingResult(
                    PingOutcome.Ignored,
                    IsRunning: true,
                    IsQueued: true
                );
            }
            finally
            {
                _lock.Release();
            }
        }

        // how long to wait before retrying after git rejected our commit - without this
        // a persistent failure becomes a tight loop against git and the LLM API
        private static readonly TimeSpan ConflictRetryDelay = TimeSpan.FromSeconds(30);

        // runs one pass, then decides whether to queue another
        private async Task DoWorkAsync()
        {
            PassOutcome outcome = await DoOnePassAsync();

            switch (outcome)
            {
                case PassOutcome.RemovedStaleFiles:
                    Ping();
                    break;

                case PassOutcome.Generated:
                    // each pass advances a worksheet one step, so keep going until converged
                    RollbackBackoffStrategy();
                    Ping();
                    break;

                case PassOutcome.GitConflict:
                    Backoff();
                    _logger.LogInformation(
                        "backing off for {Seconds}s before retrying",
                        ConflictRetryDelay.TotalSeconds);
                    await Task.Delay(ConflictRetryDelay);
                    Ping();
                    break;

                case PassOutcome.GenerationFailed:
                    // don't requeue - that would just burn LLM calls on the same failure
                    _logger.LogError("pass produced nothing, not queueing another run");
                    break;

                case PassOutcome.NothingToDo:
                    break;
            }
        }

        private void StartTask()
        {
            // Run in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await DoWorkAsync();
                    _logger.LogInformation("work complete");
                } catch (Exception e)
                {
                    _logger.LogCritical($"failed to performed work with error {e.Message}");
                }
                finally
                {
                    await OnTaskCompletedAsync();
                }
            });
        }



        private async Task OnTaskCompletedAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_isQueued)
                {
                    _isQueued = false;
                    StartTask(); // immediately start queued task
                }
                else
                {
                    _isRunning = false;
                }
            }
            finally
            {
                _lock.Release();
                _logger.LogInformation("work lock released");
            }
        }

    }
}
