using Shared;
using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        // One file someone has asked for, with the order they asked in. Held in memory
        // only: a request that is lost to a restart can be made again, and once the file
        // exists the repository is the record of it.
        internal record PendingRequest
        {
            internal required RootName Root { get; init; }
            internal required SourceKey Key { get; init; }
            internal required long Sequence { get; init; }
        }

        private readonly List<PendingRequest> _requests = new();

        private readonly SemaphoreSlim _requestLock = new(1, 1);

        private long _nextSequence;

        // Accepts a request for one translated file and queues everything it is derived
        // from alongside it. First come first served: the sequence number is what orders
        // one request against another, and requested work is done before anything the
        // pipeline chose for itself.
        public GenerateResult RequestGeneration(GenerateRequest request)
        {
            SourceMetadata? target = Interpret(request, out String why);

            if (target is null)
            {
                _logger.LogWarning("rejected a generation request: {Why}", why);

                return new GenerateResult(GenerateOutcome.NotUnderstood, why, Array.Empty<String>());
            }

            IReadOnlyList<PlannedSource> plan =
                SourcePlan.For(target.Archetype, Languages, L2Settings.GenerateVocabularyKeys);

            SourceKey wanted = KeyOf(target);

            List<SourceKey> closure = Closure(plan, wanted);

            if (closure.Count == 0)
            {
                why = $"{request.Rendition} in {request.Language} is not something "
                    + $"{target.RootName} can have";

                return new GenerateResult(GenerateOutcome.NotUnderstood, why, Array.Empty<String>());
            }

            List<String> queued = new();

            _requestLock.Wait();
            try
            {
                long sequence = _nextSequence++;

                foreach (SourceKey key in closure)
                {
                    // a file already asked for keeps its original place in the queue
                    if (_requests.Any(r => r.Root == target.RootName && r.Key.Equals(key)))
                    {
                        continue;
                    }

                    _requests.Add(new PendingRequest
                    {
                        Root     = target.RootName,
                        Key      = key,
                        Sequence = sequence,
                    });
                }

                queued = closure
                    .Select(k => GetFilenameFromMetadata(target with
                    {
                        Language  = k.Language,
                        Type      = k.Type,
                        Rendition = k.Rendition,
                    }))
                    .ToList();
            }
            finally
            {
                _requestLock.Release();
            }

            _logger.LogInformation(
                "queued {Count} file(s) for {Root} in {Language}: {Files}",
                queued.Count, target.RootName, request.Language, String.Join(", ", queued));

            Ping();

            return new GenerateResult(
                GenerateOutcome.Queued,
                $"queued {queued.Count} file(s); generation has started",
                queued);
        }

        // Reads the request into metadata, saying plainly what was wrong rather than
        // throwing - these come from outside, so a malformed one is expected traffic.
        private SourceMetadata? Interpret(GenerateRequest request, out String why)
        {
            why = String.Empty;

            if (String.IsNullOrWhiteSpace(request.RootName))
            {
                why = "no root name was given";
                return null;
            }

            if (!Enum.TryParse(request.Type, ignoreCase: true, out SourceType type))
            {
                why = $"'{request.Type}' is not a part of a sheet - use Root, WorkedSolutions or Solutions";
                return null;
            }

            if (!Enum.TryParse(request.Rendition, ignoreCase: true, out SourceRendition rendition)
                || rendition is not (SourceRendition.ParallelText or SourceRendition.Tier3Only))
            {
                why = $"'{request.Rendition}' is not a translated form - use ParallelText or Tier3Only";
                return null;
            }

            ISO639_3Code language = new(request.Language ?? String.Empty);

            if (!Languages.CanGenerate(language))
            {
                why = $"'{request.Language}' is not a language we can generate - it needs an entry "
                    + "in L2:Languages with a font and a babel name";
                return null;
            }

            // the archetype comes from the folder the root lives in, as it does everywhere
            SourceMetadata parsed = ParseMetadataFromFilename(request.RootName, _logger);

            return new SourceMetadata
            {
                RootName  = request.RootName,
                Archetype = parsed.Archetype,
                Language  = language,
                Type      = type,
                Rendition = rendition,
            };
        }

        // everything that has to exist before the wanted file can be made, deepest first,
        // with the wanted file last
        private static List<SourceKey> Closure(
            IReadOnlyList<PlannedSource> plan, SourceKey wanted)
        {
            var byKey = plan.ToDictionary(p => p.Key);

            if (!byKey.ContainsKey(wanted)) { return new List<SourceKey>(); }

            List<SourceKey> ordered = new();
            HashSet<SourceKey> seen = new();

            void Visit(SourceKey key)
            {
                if (!seen.Add(key)) { return; }

                if (!byKey.TryGetValue(key, out PlannedSource? planned)) { return; }

                foreach (SourceKey dependency in planned.DependsOn) { Visit(dependency); }

                // the English root is written by a person, so it is never queued - if it
                // is missing the request simply waits, which is the honest outcome
                if (!planned.Written) { ordered.Add(key); }
            }

            Visit(wanted);

            return ordered;
        }

        #region What the batch selector asks

        // whether this file has been asked for, and if so how long ago
        internal long? RequestedAt(RootName root, SourceKey key)
        {
            _requestLock.Wait();
            try
            {
                return _requests
                    .Where(r => r.Root == root && r.Key.Equals(key))
                    .Select(r => (long?)r.Sequence)
                    .FirstOrDefault();
            }
            finally
            {
                _requestLock.Release();
            }
        }

        // A request is satisfied once the file is there. It is dropped rather than kept,
        // so that a later edit to the English sheet removes the file without it being
        // silently rebuilt - a file made on request is maintained only while it lasts.
        internal void ForgetSatisfiedRequests(Dictionary<RootName, RootPlanState> states)
        {
            _requestLock.Wait();
            try
            {
                _requests.RemoveAll(r =>
                    !states.TryGetValue(r.Root, out RootPlanState? state)
                    || !state.IsMissing(r.Key));
            }
            finally
            {
                _requestLock.Release();
            }
        }

        #endregion

        // The languages that can actually be produced, in the order they are configured.
        // The website offers these and no others, so it cannot invite someone to ask for
        // a language that would fail the moment it was tried.
        public IReadOnlyList<LanguageInfo> SupportedLanguages()
        {
            return Languages.All
                .Select(code => Languages.Get(code)!)
                .Select(profile => new LanguageInfo(
                    profile.Code.Code,
                    profile.TitleName,
                    profile.RightToLeft,
                    Languages.EagerLanguages.Contains(profile.Code)))
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        #region Purging

        // Removes the generated translations so they are built again from the settings in
        // force now. The provenance block makes most of this unnecessary - a colour
        // change rebuilds only what it affects - but a rework big enough that the whole
        // lot should go is exactly what this is for.
        public async Task<PurgeResult> PurgeAsync(PurgeScope scope)
        {
            RepoModelSnapshot snapshot = SnapshotForPurge();

            List<String> doomed = snapshot.Files
                .Where(f => ShouldPurge(f.SourceMetadata.Rendition, scope))
                .Select(f => f.TrackedFile.FullPath)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            if (doomed.Count == 0)
            {
                return new PurgeResult(true, "there was nothing to remove", doomed);
            }

            _logger.LogInformation("purging {Count} generated file(s)", doomed.Count);

            bool removed = await RepoManager.RemoveFiles(doomed, snapshot.Hash);

            if (!removed)
            {
                return new PurgeResult(
                    false, "the repository moved under us, nothing was removed", Array.Empty<String>());
            }

            // whatever was eager comes back on the next pass; anything that had been
            // asked for does not, which is the same rule as for a stale file
            Ping();

            return new PurgeResult(
                true, $"removed {doomed.Count} file(s); generation has started", doomed);
        }

        private static bool ShouldPurge(SourceRendition rendition, PurgeScope scope)
        {
            switch (rendition)
            {
                case SourceRendition.Original:
                    return false;

                case SourceRendition.VocabKey:
                    return scope == PurgeScope.TranslationsAndVocabulary;

                default:
                    return true;
            }
        }

        private record RepoModelSnapshot(List<TrackedFileWithMetadata> Files, String Hash);

        private RepoModelSnapshot SnapshotForPurge()
        {
            var model = RepoManager.GetLatestModelOfRepo();

            var files = GetVariantInfo(model, "tex")
                .SelectMany(kvp => kvp.Value)
                .ToList();

            return new RepoModelSnapshot(files, model.LastCommitHash);
        }

        #endregion
    }
}
