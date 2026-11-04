using SyntheticPDFs.Models.Content;

namespace SyntheticPDFs.Logic
{
    // What is still to do, pass by pass, found by running the batch rules forward over the
    // plans rather than by counting missing files. A pass does one tier of work and a file
    // waits for everything it is made from, so twenty missing files can be one pass or six.
    //
    // Rough by nature. Every file is assumed to come back first time, and nobody is assumed
    // to push anything meanwhile. What a vocabulary key will add to the dictionary cannot be
    // known until it is written, so the dictionary passes that usually follow a round of
    // keys are expected rather than known.
    public partial class Orchestrator
    {
        // Waiting counts files that will never be ready, because something they are made
        // from is not going to be made either
        internal record Forecast(IReadOnlyList<ForecastPass> Passes, int Waiting)
        {
            internal static Forecast Nothing { get; } = new(Array.Empty<ForecastPass>(), 0);
        }

        // Work is null for the pass that removes out-of-date files, which makes nothing
        internal record ForecastPass(GenerationPriority? Work, IReadOnlyList<ForecastJob> Jobs);

        // Kind is the work a job would be without a request, which is what decides how long
        // it takes. null for a file being removed
        internal record ForecastJob(String Description, GenerationPriority? Kind);

        // a forecast that never ends would be a bug, and a status page that hangs is a worse
        // way to find one than a forecast cut short
        private const int MostPassesForecast = 1000;

        private const String ExpectedMark = " (expected)";

        private sealed class Job
        {
            internal required String Root { get; init; }

            // null for work on a file already there - a check, or a restatement
            internal ContentKey? Makes { get; init; }

            internal IReadOnlyList<ContentKey> DependsOn { get; init; } = Array.Empty<ContentKey>();

            internal required GenerationPriority Priority { get; init; }
            internal required GenerationPriority Kind { get; init; }

            // set only for requested work, which keeps the place it was asked for in
            internal long? RequestedAt { get; init; }

            internal required int RootOrder { get; init; }
            internal required String Description { get; init; }

            // an owed check, which the rest of its sheet waits for
            internal bool Blocks { get; init; }

            internal bool Done { get; set; }
        }

        // Numbered the way CandidatesFor numbers them, afresh each pass over what is ready,
        // so the first pass forecast is the batch the pass is about to choose.
        internal Forecast ForecastFrom(ContentModel model, int batchSize)
        {
            Dictionary<(String Root, ContentKey Key), long> requested = RequestedSnapshot();

            List<Job> jobs = new();

            int rootOrder = 0;

            foreach (SheetState sheet in model.Sheets.Values)
            {
                jobs.AddRange(JobsFor(sheet, rootOrder, requested));

                rootOrder++;
            }

            jobs.AddRange(DictionaryJobs(
                model, rootOrder,
                keysToCome: jobs.Any(j => j.Kind == GenerationPriority.EnglishGlossary)));

            List<ForecastPass> passes = new();

            List<ContentFile> stale = model.Sheets.Values.SelectMany(s => s.StaleFiles).ToList();

            // a pass that finds anything stale removes it and does nothing else
            if (stale.Count > 0)
            {
                passes.Add(new ForecastPass(
                    null, stale.Select(f => new ForecastJob(Shortened(f.FullPath), null)).ToList()));
            }

            HashSet<(String, ContentKey)> made = new();

            bool Settled(String root, ContentKey key) =>
                made.Contains((root, key))
                || (model.Sheets.TryGetValue(root, out SheetState? sheet) && sheet.IsSettled(key));

            while (passes.Count < MostPassesForecast)
            {
                List<(Job Job, long Sequence)> ready = new();

                foreach (IGrouping<String, Job> root in jobs.Where(j => !j.Done).GroupBy(j => j.Root))
                {
                    bool checking = root.Any(j => j.Blocks);

                    long order = 0;

                    foreach (Job job in root)
                    {
                        if (checking && !job.Blocks) { continue; }

                        if (!job.DependsOn.All(dependency => Settled(job.Root, dependency))) { continue; }

                        ready.Add((job, job.Blocks ? 0 : job.RequestedAt ?? order++));
                    }
                }

                if (ready.Count == 0) { break; }

                GenerationPriority most = ready.Min(r => r.Job.Priority);

                List<Job> chosen = ready
                    .Where(r => r.Job.Priority == most)
                    .OrderBy(r => r.Sequence)
                    .ThenBy(r => r.Job.RootOrder)
                    .Take(batchSize)
                    .Select(r => r.Job)
                    .ToList();

                foreach (Job job in chosen)
                {
                    job.Done = true;

                    if (job.Makes is ContentKey key) { made.Add((job.Root, key)); }
                }

                passes.Add(new ForecastPass(
                    most, chosen.Select(j => new ForecastJob(j.Description, j.Kind)).ToList()));
            }

            return new Forecast(passes, jobs.Count(j => !j.Done));
        }

        // Everything one sheet is owed: its checks, any key being put back into step, and
        // every planned file the pipeline would make that it has not got. A sheet whose
        // English root is gone is left out, since none of it is coming back - its files are
        // removed as stale instead.
        private List<Job> JobsFor(
            SheetState sheet,
            int rootOrder,
            IReadOnlyDictionary<(String Root, ContentKey Key), long> requested)
        {
            List<Job> jobs = new();

            if (sheet.Plan.Any(p => p.Written && sheet.IsMissing(p.Key))) { return jobs; }

            foreach (GenerationRequest check in OutstandingChecks(sheet))
            {
                jobs.Add(new Job
                {
                    Root        = sheet.RootName,
                    Priority    = GenerationPriority.English,
                    Kind        = GenerationPriority.English,
                    RootOrder   = rootOrder,
                    Description = Describe(check),
                    Blocks      = true,
                });
            }

            foreach ((String root, ContentKey key) in _restating)
            {
                if (root != sheet.RootName) { continue; }

                jobs.Add(new Job
                {
                    Root        = root,
                    Priority    = PriorityOf(key),
                    Kind        = PriorityOf(key),
                    RootOrder   = rootOrder,
                    Description = Describe(Request(
                        root, key, sheet.Archetype, GenerationJob.RestateGlossary)),
                });
            }

            foreach (PlannedFile planned in sheet.Plan)
            {
                if (sheet.IsSettled(planned.Key)) { continue; }

                long? requestedAt =
                    requested.TryGetValue((sheet.RootName, planned.Key), out long at) ? at : null;

                if (Wanted(planned, requestedAt) is not { } priority) { continue; }

                jobs.Add(new Job
                {
                    Root        = sheet.RootName,
                    Makes       = planned.Key,
                    DependsOn   = planned.DependsOn,
                    Priority    = priority,
                    Kind        = PriorityOf(planned.Key),
                    RequestedAt = requestedAt,
                    RootOrder   = rootOrder,
                    Description = Describe(Request(sheet.RootName, planned.Key, sheet.Archetype)),
                });
            }

            return jobs;
        }

        // What the dictionaries need now, and - when vocabulary keys are about to be written -
        // what a round of keys usually brings: new words for the shared dictionary, and those
        // words translated into every dictionary kept in step with it.
        private List<Job> DictionaryJobs(ContentModel model, int rootOrder, bool keysToCome)
        {
            List<Job> jobs = DictionaryCandidates(model, rootOrder)
                .Select(c => DictionaryWork(c.Request, c.Priority, rootOrder, expected: false))
                .ToList();

            if (!keysToCome) { return jobs; }

            HashSet<String> known = jobs.Select(j => j.Description).ToHashSet();

            foreach (DictionaryState state in model.Dictionaries.Values)
            {
                if (!state.Exists) { continue; }

                List<(GenerationRequest Request, GenerationPriority Priority)> usual = new()
                {
                    (DictionaryJob(
                        state.RootName, ISO639_3Code.eng,
                        SheetForm.Original, GenerationJob.ExtendDictionary),
                     GenerationPriority.SharedDictionary),
                };

                foreach (ISO639_3Code language in LanguagesWantingADictionary(model, state))
                {
                    usual.Add((DictionaryJob(
                        state.RootName, language,
                        SheetForm.ParallelText, GenerationJob.RefreshDictionary),
                     GenerationPriority.TranslatedDictionary));
                }

                foreach ((GenerationRequest request, GenerationPriority priority) in usual)
                {
                    if (known.Contains(Describe(request))) { continue; }

                    jobs.Add(DictionaryWork(request, priority, rootOrder, expected: true));
                }
            }

            return jobs;
        }

        private Job DictionaryWork(
            GenerationRequest request, GenerationPriority priority, int rootOrder, bool expected) =>
            new Job
            {
                Root        = request.Target.RootName,
                Priority    = priority,
                Kind        = priority,
                RootOrder   = rootOrder,
                Description = Describe(request) + (expected ? ExpectedMark : ""),
            };

        private Dictionary<(String Root, ContentKey Key), long> RequestedSnapshot()
        {
            _requestLock.Wait();
            try
            {
                Dictionary<(String Root, ContentKey Key), long> requested = new();

                foreach (PendingRequest request in _requests)
                {
                    requested.TryAdd((request.Root, request.Key), request.Sequence);
                }

                return requested;
            }
            finally
            {
                _requestLock.Release();
            }
        }

        // a job as a person would want it listed: the file, and what is being done to it
        // when that is anything other than writing it
        internal String Describe(GenerationRequest request)
        {
            String path = Shortened(request.Target.FilePath);

            return request.Job switch
            {
                GenerationJob.CheckAnswerMacros => $"{path} - checking the answer overlays",
                GenerationJob.RefreshDictionary => $"{path} - bringing it into step with the shared dictionary",
                GenerationJob.ExtendDictionary  => $"{path} - adding new words",
                GenerationJob.RestateGlossary   => $"{path} - restating it from the dictionary",
                _                               => path,
            };
        }

        // without the source directory every file in the repository shares
        internal String Shortened(String path)
        {
            String prefix = ContentRepository.SourceDirectory.TrimEnd('/') + "/";

            return prefix.Length > 1 && path.StartsWith(prefix, StringComparison.Ordinal)
                ? path[prefix.Length..]
                : path;
        }
    }
}
