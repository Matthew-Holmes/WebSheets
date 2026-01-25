using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public sealed record PingResult(
        PingOutcome Outcome,
        bool IsRunning,
        bool IsQueued
    );
}
