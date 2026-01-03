namespace SyntheticPDFs.Logic
{
    public sealed record PingResult(
        PingOutcome Outcome,
        bool IsRunning,
        bool IsQueued
    );

}
