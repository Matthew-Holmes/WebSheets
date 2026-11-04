using Shared;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        // What the pipeline is doing and what it has left, for the website's pipeline page.
        // Reading it changes nothing.
        public PipelineStatus Status()
        {
            bool running;
            bool queued;

            _lock.Wait();
            try
            {
                running = _isRunning;
                queued = _isQueued;
            }
            finally
            {
                _lock.Release();
            }

            List<RequestedFile> requests;

            _requestLock.Wait();
            try
            {
                requests = _requests
                    .OrderBy(r => r.Sequence)
                    .Select(r => new RequestedFile(Shortened(r.File), r.AskedAt))
                    .ToList();
            }
            finally
            {
                _requestLock.Release();
            }

            return Monitor.Snapshot(
                running, queued, requests, DictionaryProblems, LLMService.TokensUsed);
        }
    }
}
