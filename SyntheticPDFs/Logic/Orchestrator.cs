using SyntheticPDFs.Git;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        private readonly SemaphoreSlim _lock = new(1, 1);

        private bool _isRunning;
        private bool _isQueued; 

        private GitRepoManager RepoManager { get; set; }


        private readonly ILogger<Orchestrator> _logger;

        public Orchestrator(ILogger<Orchestrator> logger, GitRepoManager repoManager)
        {
            _logger = logger;

            RepoManager = repoManager;
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

        private void StartTask()
        {
            // Run in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await DoWorkAsync();
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
            }
        }

    }
}
