namespace SyntheticPDFs.Models.Content
{
    // Everything the repository holds for one root, across every language and form,
    // judged against the plan its archetype gives it.
    //
    // A whole root at once rather than one language at a time, because the dependencies
    // cross languages: a translated glossary is derived from the English one, and a
    // parallel text from its English counterpart. Splitting by language first would hide
    // exactly the edges that decide whether a translated file is still current.
    //
    // Immutable. It arrives from ContentModel knowing only which files exist, and
    // Judged() returns the same state with the plan applied - so the cheap half, reading
    // names, is separable from the half that has to open files.
    internal sealed record SheetState
    {
        internal required String RootName { get; init; }

        internal required SheetArchetype Archetype { get; init; }

        // everything present for this root, whether the plan allows it or not
        internal required IReadOnlyDictionary<ContentKey, ContentFile> Files { get; init; }

        // empty until Judged has been called
        internal IReadOnlyList<PlannedFile> Plan { get; init; } = Array.Empty<PlannedFile>();

        // present but no longer trustworthy - a dependency is missing, or has been edited
        // since, or the file is not something this archetype may have at all
        internal IReadOnlyList<ContentFile> StaleFiles { get; init; } = Array.Empty<ContentFile>();

        private IReadOnlySet<ContentKey> Stale { get; init; } = new HashSet<ContentKey>();

        #region Asking about it

        // a file is settled when it is there and nothing about it is in doubt, which is
        // the condition for anything derived from it to be worth making
        internal bool IsSettled(ContentKey key) =>
            Files.ContainsKey(key) && !Stale.Contains(key);

        internal bool IsMissing(ContentKey key) => !Files.ContainsKey(key);

        internal ContentFile? File(ContentKey key) =>
            Files.TryGetValue(key, out ContentFile? file) ? file : null;

        internal ContentFile? File(SheetPart part, SheetForm form) =>
            File(new ContentKey(ISO639_3Code.eng, part, form));

        // What could be created right now: planned, absent, and everything it is derived
        // from already settled. Nothing here depends on a file this same pass is about to
        // write, so a batch drawn from it is safe to run concurrently and safe to commit
        // together.
        internal IEnumerable<PlannedFile> Creatable() =>
            Plan.Where(p => !p.Written && IsMissing(p.Key) && p.DependsOn.All(IsSettled));

        #endregion

        #region Judging it against its plan

        internal SheetState Judged(
            LanguageTable languages,
            bool includeGlossaries,

            // files already known to be out of date for a reason age cannot show - a
            // definition that has been reworded, or a colour that has changed. they seed
            // the walk below, so whatever was derived from them goes too
            IReadOnlySet<ContentKey>? outdated = null)
        {
            return Judged(Archetype.Plan(languages, includeGlossaries), outdated);
        }

        internal SheetState Judged(
            IReadOnlyList<PlannedFile> plan, IReadOnlySet<ContentKey>? outdated = null)
        {
            HashSet<ContentKey> planned = plan.Select(p => p.Key).ToHashSet();

            // something this archetype may not have - a poster that was given worked
            // solutions before posters stopped getting them, or a translation into a
            // language that is no longer configured. it has no parent to be judged
            // against, so it goes without further ceremony
            List<ContentFile> unplanned = Files
                .Where(kvp => !planned.Contains(kvp.Key))
                .Select(kvp => kvp.Value)
                .ToList();

            Dictionary<ContentKey, ContentFile> present = Files
                .Where(kvp => planned.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            HashSet<ContentKey> stale = FindStale(plan, present, outdated);

            return this with
            {
                Plan       = plan,
                Stale      = stale,
                StaleFiles = stale.Select(k => present[k]).Concat(unplanned).ToList(),
            };
        }

        // Walks the plan in order, so a file is judged after everything it depends on.
        // Staleness is transitive: worked solutions older than the root take the answer
        // key with them, even though the key is younger than both.
        private static HashSet<ContentKey> FindStale(
            IReadOnlyList<PlannedFile> plan,
            IReadOnlyDictionary<ContentKey, ContentFile> present,
            IReadOnlySet<ContentKey>? outdated)
        {
            HashSet<ContentKey> stale = new();

            if (outdated is not null)
            {
                foreach (ContentKey key in outdated)
                {
                    if (present.ContainsKey(key)) { stale.Add(key); }
                }
            }

            foreach (PlannedFile planned in plan)
            {
                if (!present.TryGetValue(planned.Key, out ContentFile? file)) { continue; }

                if (stale.Contains(planned.Key)) { continue; }

                foreach (ContentKey dependency in planned.DependsOn)
                {
                    if (!present.TryGetValue(dependency, out ContentFile? parent))
                    {
                        // orphaned - whatever it was derived from has gone
                        stale.Add(planned.Key);
                        break;
                    }

                    if (stale.Contains(dependency) || !IsYounger(file, parent))
                    {
                        stale.Add(planned.Key);
                        break;
                    }
                }
            }

            return stale;
        }

        // lower age == younger, since age counts commits back from HEAD. leq rather than
        // lt, since a batch lands in one commit and the adder is assumed to have
        // respected causality within it
        private static bool IsYounger(ContentFile file, ContentFile thanParent) =>
            file.AgeCommits <= thanParent.AgeCommits;

        #endregion
    }
}
