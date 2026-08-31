using System.Collections.Generic;

namespace Shared
{
    public sealed record PingResult(
        PingOutcome Outcome,
        bool IsRunning,
        bool IsQueued,

        // Anything in the content repository the generator could not read, found on the
        // most recent completed pass. In practice this is a dictionary somebody has
        // edited into a shape the parser cannot follow.
        //
        // It is reported here, and the endpoint answers with an error status when it is
        // not empty, so that the build which pushed the bad edit says so rather than
        // leaving it to be noticed months later by a translation that quietly stopped
        // being applied. It describes the pass before this ping, not the one this ping
        // has just started - a problem introduced by the push being announced here is
        // surfaced by the next announcement.
        IReadOnlyList<string> Problems
    )
    {
        public PingResult(PingOutcome outcome, bool isRunning, bool isQueued)
            : this(outcome, isRunning, isQueued, new List<string>())
        {
        }
    }
}
