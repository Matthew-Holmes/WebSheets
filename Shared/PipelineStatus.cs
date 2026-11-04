namespace Shared
{
    // What the generator is doing, what it has left, and roughly how long that will take,
    // for the website's pipeline page. One constructor per record: the website reads these
    // with System.Text.Json, which will not choose between two.

    public enum PipelineState
    {
        Idle,            // nothing to do until something pings it
        Working,         // a pass is running, or about to
        WaitingToRetry,  // git refused a commit, so it is backing off before trying again
        Stopped,         // a pass made nothing, so no further pass was queued
    }

    public sealed record PipelineStatus(
        PipelineState State,
        string Activity,                         // what it is doing now, in words
        DateTimeOffset? ActivitySince,
        bool AnotherPassQueued,
        PassProgress? CurrentPass,
        PipelineForecast? Forecast,              // null when there is nothing left to do
        IReadOnlyList<PassRecord> RecentPasses,  // newest first
        IReadOnlyList<RequestedFile> Requests,   // asked for on the website, oldest first
        IReadOnlyList<string> Problems,          // files in the repository it could not read
        long PromptTokens,                       // since the generator started
        long CompletionTokens,
        DateTimeOffset RunningSince,
        DateTimeOffset At);                      // when this was read

    public enum PassItemState
    {
        Running,
        Done,
        Failed,
    }

    // one file of the pass running now
    public sealed record PassItem(
        string File,
        PassItemState State,
        TimeSpan Elapsed,
        string? Error);

    public sealed record PassProgress(
        int Number,
        string Work,                    // the kind of work, in words
        DateTimeOffset Started,
        IReadOnlyList<PassItem> Items); // empty until it has chosen what to make

    public enum PassResult
    {
        Generated,
        RemovedStaleFiles,
        NothingToDo,
        RepositoryMoved,   // git refused the commit, so the pass is retried
        AllFailed,         // nothing in the batch came back, so no pass was queued after it
        Crashed,
    }

    public sealed record PassRecord(
        int Number,
        string Work,
        DateTimeOffset Started,
        TimeSpan Took,
        PassResult Result,
        int Made,
        int Failed,
        int Removed,
        long PromptTokens,
        long CompletionTokens,
        string? Error);

    // Everything still to do, as the pipeline's own rules would order it. Rough: what a
    // vocabulary key will add to the dictionary cannot be known until it is written, and
    // a failure or an edit meanwhile changes the lot.
    public sealed record PipelineForecast(
        int Files,
        int Passes,
        TimeSpan Remaining,
        int Waiting,                             // files waiting on something that will not be made
        IReadOnlyList<ForecastStage> Stages);

    // consecutive passes doing the same kind of work
    public sealed record ForecastStage(
        string Work,
        int Passes,
        int FileCount,
        TimeSpan Estimated,
        int TimedPasses,               // passes of this kind timed since the generator started; 0 means a typical figure
        IReadOnlyList<string> Jobs);   // may stop short of FileCount on a long forecast

    public sealed record RequestedFile(
        string File,
        DateTimeOffset AskedAt);
}
