namespace SyntheticPDFs.Logic
{
    public enum PingOutcome
    {
        Started,   // task was started by this ping
        Queued,    // task was queued by this ping
        Ignored    // nothing changed (already running + queued)
    }

}
