using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public enum PingOutcome
    {
        Started,   // task was started by this ping
        Queued,    // task was queued by this ping
        Ignored    // nothing changed (already running + queued)
    }

}
