using Shared;
using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    // The record the status page is built from: the pass running now, the ones just
    // finished, and the forecast made at the start of the latest. Written by the pass and
    // read by status requests on other threads, so everything goes through one lock.
    internal sealed class PipelineMonitor
    {
        private readonly object _gate = new();

        private readonly TimeProvider _time;

        // enough to see the shape of the last run or two
        private const int PassesKept = 30;

        // how many recent passes of a kind its expected duration is taken from
        private const int PassesTimed = 10;

        // a long forecast is summarised rather than listed in full
        private const int JobsListed = 200;

        private const int ErrorLength = 300;

        // How long a pass of each kind takes before any has been timed: the medians of
        // the gaps between the generator's own commits, September to November 2026.
        private static readonly IReadOnlyDictionary<GenerationPriority, TimeSpan> Typical =
            new Dictionary<GenerationPriority, TimeSpan>
            {
                [GenerationPriority.English]              = TimeSpan.FromSeconds(64),
                [GenerationPriority.EnglishGlossary]      = TimeSpan.FromSeconds(85),
                [GenerationPriority.SharedDictionary]     = TimeSpan.FromSeconds(18),
                [GenerationPriority.TranslatedDictionary] = TimeSpan.FromSeconds(54),
                [GenerationPriority.TranslatedGlossary]   = TimeSpan.FromSeconds(21),
                [GenerationPriority.Translated]           = TimeSpan.FromSeconds(409),
            };

        private static readonly TimeSpan TypicalRemoval = TimeSpan.FromSeconds(20);

        private sealed class Item
        {
            internal required String File { get; init; }
            internal required DateTimeOffset Started { get; init; }
            internal DateTimeOffset? Finished { get; set; }
            internal String? Error { get; set; }
        }

        private sealed class Running
        {
            internal required int Number { get; init; }
            internal required DateTimeOffset Started { get; init; }
            internal required (long Prompt, long Completion) TokensAtStart { get; init; }
            internal required String Activity { get; set; }
            internal required DateTimeOffset ActivitySince { get; set; }

            // null until it has chosen what kind of work to do, and for a pass that only removes
            internal GenerationPriority? Work { get; set; }

            internal List<Item> Items { get; } = new();
            internal int Removed { get; set; }
            internal int Pushed { get; set; }
        }

        // what a finished pass did, kept with what it was so that its time can be reused
        private sealed record Finished(PassRecord Record, GenerationPriority? Work);

        private readonly List<Finished> _finished = new();

        private Running? _current;

        private int _passes;

        private Forecast _forecast = Forecast.Nothing;

        // the pass the forecast was made in, which is the first pass it counts
        private int _forecastMadeIn;

        private DateTimeOffset? _retryAt;

        internal DateTimeOffset RunningSince { get; }

        internal PipelineMonitor(TimeProvider time)
        {
            _time = time;
            RunningSince = time.GetUtcNow();
        }

        private DateTimeOffset Now => _time.GetUtcNow();

        #region What the pass reports

        internal void PassStarted((long Prompt, long Completion) tokens)
        {
            lock (_gate)
            {
                DateTimeOffset now = Now;

                _retryAt = null;

                _current = new Running
                {
                    Number        = ++_passes,
                    Started       = now,
                    TokensAtStart = tokens,
                    Activity      = "reading the repository",
                    ActivitySince = now,
                };
            }
        }

        // the forecast made by the latest pass, whatever state the pipeline is in now
        internal Forecast LatestForecast
        {
            get { lock (_gate) { return _forecast; } }
        }

        internal void Forecasted(Forecast forecast)
        {
            lock (_gate)
            {
                _forecast = forecast;
                _forecastMadeIn = _current?.Number ?? _passes;
            }
        }

        internal void Removing(int files)
        {
            lock (_gate)
            {
                if (_current is null) { return; }

                _current.Removed = files;
                Doing($"removing {Files(files)} that no longer match what they were made from");
            }
        }

        internal void Generating(GenerationPriority work, IReadOnlyList<String> files)
        {
            lock (_gate)
            {
                if (_current is null) { return; }

                DateTimeOffset now = Now;

                _current.Work = work;
                _current.Items.Clear();
                _current.Items.AddRange(files.Select(f => new Item { File = f, Started = now }));

                Doing($"making {Files(files.Count)}: {Words(work)}");
            }
        }

        // error is null when the item came back with something to commit
        internal void ItemFinished(int index, String? error)
        {
            lock (_gate)
            {
                if (_current is null || index < 0 || index >= _current.Items.Count) { return; }

                Item item = _current.Items[index];

                item.Finished = Now;
                item.Error = error is null ? null : Shortened(error);
            }
        }

        internal void Pushing(int files)
        {
            lock (_gate)
            {
                if (_current is null) { return; }

                _current.Pushed = files;
                Doing($"committing and pushing {Files(files)}");
            }
        }

        internal void PassFinished(
            PassResult result, (long Prompt, long Completion) tokens, String? error = null)
        {
            lock (_gate)
            {
                if (_current is null) { return; }

                Running pass = _current;

                PassRecord record = new(
                    Number:           pass.Number,
                    Work:             pass.Work is { } work
                                        ? Words(work)
                                        : result == PassResult.RemovedStaleFiles
                                            ? RemovalWords
                                            : "nothing chosen",
                    Started:          pass.Started,
                    Took:             Now - pass.Started,
                    Result:           result,
                    Made:             result == PassResult.Generated ? pass.Pushed : 0,
                    Failed:           pass.Items.Count(i => i.Error is not null),
                    Removed:          result == PassResult.RemovedStaleFiles ? pass.Removed : 0,
                    PromptTokens:     tokens.Prompt - pass.TokensAtStart.Prompt,
                    CompletionTokens: tokens.Completion - pass.TokensAtStart.Completion,
                    Error:            error is null ? null : Shortened(error));

                _finished.Insert(0, new Finished(record, pass.Work));

                if (_finished.Count > PassesKept) { _finished.RemoveAt(_finished.Count - 1); }

                _current = null;
            }
        }

        internal void WaitingToRetry(TimeSpan delay)
        {
            lock (_gate)
            {
                _retryAt = Now + delay;
            }
        }

        private void Doing(String activity)
        {
            _current!.Activity = activity;
            _current.ActivitySince = Now;
        }

        #endregion

        #region What a status request reads

        internal PipelineStatus Snapshot(
            bool running,
            bool queued,
            IReadOnlyList<RequestedFile> requests,
            IReadOnlyList<String> problems,
            (long Prompt, long Completion) tokens)
        {
            lock (_gate)
            {
                DateTimeOffset now = Now;

                PassResult? last = _finished.Count > 0 ? _finished[0].Record.Result : null;

                bool retrying = _retryAt is { } at && at > now;

                PipelineState state =
                      _current is not null ? PipelineState.Working
                    : retrying             ? PipelineState.WaitingToRetry
                    : running              ? PipelineState.Working
                    : last is PassResult.AllFailed or PassResult.Crashed
                                           ? PipelineState.Stopped
                                           : PipelineState.Idle;

                (String activity, DateTimeOffset? since) = state switch
                {
                    PipelineState.Working when _current is not null
                        => (_current.Activity, (DateTimeOffset?)_current.ActivitySince),

                    PipelineState.Working
                        => ("starting the next pass", null),

                    PipelineState.WaitingToRetry
                        => ("waiting to retry, since the repository moved while it was working", null),

                    PipelineState.Stopped
                        => ("stopped after a pass that made nothing - the next ping starts it again", null),

                    _   => ("waiting for a ping", null),
                };

                return new PipelineStatus(
                    State:             state,
                    Activity:          activity,
                    ActivitySince:     since,
                    AnotherPassQueued: queued,
                    CurrentPass:       Progress(now),
                    Forecast:          state is PipelineState.Working or PipelineState.WaitingToRetry
                                           ? ForecastFrom(now)
                                           : null,
                    RecentPasses:      _finished.Select(f => f.Record).ToList(),
                    Requests:          requests,
                    Problems:          problems,
                    PromptTokens:      tokens.Prompt,
                    CompletionTokens:  tokens.Completion,
                    RunningSince:      RunningSince,
                    At:                now);
            }
        }

        private PassProgress? Progress(DateTimeOffset now)
        {
            if (_current is null) { return null; }

            return new PassProgress(
                Number:  _current.Number,
                Work:    _current.Work is { } work
                             ? Words(work)
                             : _current.Removed > 0 ? RemovalWords : "deciding what to do",
                Started: _current.Started,
                Items:   _current.Items
                    .Select(i => new PassItem(
                        File:    i.File,
                        State:   i.Finished is null ? PassItemState.Running
                               : i.Error is null    ? PassItemState.Done
                                                    : PassItemState.Failed,
                        Elapsed: (i.Finished ?? now) - i.Started,
                        Error:   i.Error))
                    .ToList());
        }

        private PipelineForecast? ForecastFrom(DateTimeOffset now)
        {
            // a forecast made before the pass that has since finished counts that pass, so
            // between passes it starts from the second
            bool firstDone = _current is null
                && _finished.Count > 0
                && _finished[0].Record.Number == _forecastMadeIn
                && _finished[0].Record.Result is PassResult.Generated or PassResult.RemovedStaleFiles;

            List<ForecastPass> passes = _forecast.Passes.Skip(firstDone ? 1 : 0).ToList();

            if (passes.Count == 0) { return null; }

            List<StageBuilder> stages = new();

            int listed = 0;

            foreach (ForecastPass pass in passes)
            {
                (TimeSpan estimate, int timed) = Expected(pass);

                String work = pass.Work is { } w ? Words(w) : RemovalWords;

                if (stages.Count == 0 || stages[^1].Work != work)
                {
                    stages.Add(new StageBuilder { Work = work });
                }

                StageBuilder stage = stages[^1];

                stage.Passes++;
                stage.Files += pass.Jobs.Count;
                stage.Estimated += estimate;
                stage.Timed = Math.Max(stage.Timed, timed);

                foreach (ForecastJob job in pass.Jobs)
                {
                    if (listed >= JobsListed) { break; }

                    stage.Jobs.Add(job.Description);
                    listed++;
                }
            }

            TimeSpan remaining = stages.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Estimated);

            // the pass running now is the first one forecast, and is part way through
            if (_current is not null && _current.Number == _forecastMadeIn)
            {
                TimeSpan first = Expected(passes[0]).Estimate;
                TimeSpan elapsed = now - _current.Started;

                remaining -= elapsed < first ? elapsed : first;
            }

            if (_retryAt is { } at && at > now) { remaining += at - now; }

            return new PipelineForecast(
                Files:     passes.Sum(p => p.Work is null ? 0 : p.Jobs.Count),
                Passes:    passes.Count,
                Remaining: remaining,
                Waiting:   _forecast.Waiting,
                Stages:    stages
                    .Select(s => new ForecastStage(
                        s.Work, s.Passes, s.Files, s.Estimated, s.Timed, s.Jobs))
                    .ToList());
        }

        private sealed class StageBuilder
        {
            internal required String Work { get; init; }
            internal int Passes { get; set; }
            internal int Files { get; set; }
            internal TimeSpan Estimated { get; set; }
            internal int Timed { get; set; }
            internal List<String> Jobs { get; } = new();
        }

        #endregion

        #region How long a pass takes

        // A pass runs its batch side by side, so it takes about as long as its slowest file
        // whether it makes one or twenty. A pass of requested work is as slow as the
        // slowest kind of file in it, since asking for a translation does not make it any
        // quicker to write.
        private (TimeSpan Estimate, int Timed) Expected(ForecastPass pass)
        {
            if (pass.Work is null) { return Measured(null) ?? (TypicalRemoval, 0); }

            if (pass.Work != GenerationPriority.Requested)
            {
                return Measured(pass.Work) ?? (Typical[pass.Work.Value], 0);
            }

            return pass.Jobs
                .Select(j => j.Kind is { } kind
                    ? Measured(kind) ?? (Typical.GetValueOrDefault(kind, TimeSpan.Zero), 0)
                    : (TypicalRemoval, 0))
                .DefaultIfEmpty((TimeSpan.Zero, 0))
                .MaxBy(e => e.Item1);
        }

        // the median of the recent passes of this kind that got as far as committing, or
        // null if none has. a null kind means the passes that only removed files
        private (TimeSpan Estimate, int Timed)? Measured(GenerationPriority? kind)
        {
            List<TimeSpan> took = _finished
                .Where(f => kind is null
                    ? f.Record.Result == PassResult.RemovedStaleFiles
                    : f.Record.Result == PassResult.Generated && f.Work == kind)
                .Take(PassesTimed)
                .Select(f => f.Record.Took)
                .OrderBy(t => t)
                .ToList();

            if (took.Count == 0) { return null; }

            return (took[took.Count / 2], took.Count);
        }

        #endregion

        #region Words

        private const String RemovalWords = "removing out-of-date files";

        internal static String Words(GenerationPriority work) => work switch
        {
            GenerationPriority.Requested            => "files asked for on the website",
            GenerationPriority.English              => "English sheets",
            GenerationPriority.EnglishGlossary      => "English vocabulary keys",
            GenerationPriority.SharedDictionary     => "the shared dictionary",
            GenerationPriority.TranslatedDictionary => "translated dictionaries",
            GenerationPriority.TranslatedGlossary   => "translated vocabulary keys",
            _                                       => "translated sheets",
        };

        private static String Files(int count) => count == 1 ? "1 file" : $"{count} files";

        private static String Shortened(String text) =>
            text.Length <= ErrorLength ? text : text[..ErrorLength] + "...";

        #endregion
    }
}
