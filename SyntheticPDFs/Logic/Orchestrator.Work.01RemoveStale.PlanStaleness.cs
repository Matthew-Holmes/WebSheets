using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        // Everything the repository holds for one root, across every language and
        // rendition, judged against the plan for its archetype.
        //
        // This works a whole root at once rather than one language at a time, because
        // the dependencies cross languages: a translated key is derived from the
        // English vocabulary key, and a parallel text from its English counterpart.
        // Stratifying by language first would hide exactly the edges that matter.
        internal class RootPlanState
        {
            internal required RootName Root { get; init; }

            internal required SourceArchetype Archetype { get; init; }

            internal required IReadOnlyList<PlannedSource> Plan { get; init; }

            internal required IReadOnlyDictionary<SourceKey, TrackedFileWithMetadata> Present { get; init; }

            // present but no longer trustworthy - a dependency is missing, or has been
            // edited since, or the file is not something this archetype may have at all
            internal required IReadOnlyList<TrackedFileWithMetadata> StaleFiles { get; init; }

            private readonly HashSet<SourceKey> _stale = new();

            internal RootPlanState(HashSet<SourceKey> stale)
            {
                _stale = stale;
            }

            // a file is settled when it is there and nothing about it is in doubt, which
            // is the condition for anything derived from it to be worth making
            internal bool IsSettled(SourceKey key) =>
                Present.ContainsKey(key) && !_stale.Contains(key);

            internal bool IsMissing(SourceKey key) => !Present.ContainsKey(key);

            // what could be created right now: planned, absent, and everything it is
            // derived from already settled. nothing here depends on a file that this
            // same pass is about to write, so a batch drawn from it is safe to run
            // concurrently and safe to commit together
            internal IEnumerable<PlannedSource> Creatable()
            {
                return Plan.Where(p =>
                    !p.Written
                    && IsMissing(p.Key)
                    && p.DependsOn.All(IsSettled));
            }

            internal TrackedFileWithMetadata? File(SourceKey key) =>
                Present.TryGetValue(key, out var file) ? file : null;
        }

        // lower age == younger, since age counts commits back from HEAD
        private static bool IsYounger(TrackedFileWithMetadata A, TrackedFileWithMetadata ThanB)
        {
            // use leq since we may add in batches, and be optimistic that the adder
            // has respected causality!
            return A.TrackedFile.AgeCommits <= ThanB.TrackedFile.AgeCommits;
        }

        internal static RootPlanState BuildPlanState(
            RootName root,
            SourceArchetype archetype,
            IEnumerable<TrackedFileWithMetadata> files,
            IReadOnlyList<PlannedSource> plan,

            // files already known to be out of date for a reason age cannot show - a
            // definition that has been reworded, or a colour that has changed. they seed
            // the walk below, so whatever was derived from them goes too
            IReadOnlySet<SourceKey>? outdated = null)
        {
            Dictionary<SourceKey, TrackedFileWithMetadata> present = new();

            List<TrackedFileWithMetadata> notPlanned = new();

            HashSet<SourceKey> planned = plan.Select(p => p.Key).ToHashSet();

            foreach (TrackedFileWithMetadata file in files)
            {
                SourceKey key = KeyOf(file.SourceMetadata);

                // something this archetype may not have - a poster that was given worked
                // solutions before posters stopped getting them, or a translation into a
                // language that is no longer configured. it has no parent to be judged
                // against, so it goes without further ceremony
                if (!planned.Contains(key))
                {
                    notPlanned.Add(file);
                    continue;
                }

                present[key] = file;
            }

            HashSet<SourceKey> stale = FindStale(plan, present, outdated);

            List<TrackedFileWithMetadata> staleFiles = stale
                .Select(k => present[k])
                .Concat(notPlanned)
                .ToList();

            return new RootPlanState(stale)
            {
                Root       = root,
                Archetype  = archetype,
                Plan       = plan,
                Present    = present,
                StaleFiles = staleFiles,
            };
        }

        // walks the plan in order, so a file is judged after everything it depends on.
        // staleness is transitive: worked solutions older than the root take the answer
        // key with them, even though the key is younger than both
        private static HashSet<SourceKey> FindStale(
            IReadOnlyList<PlannedSource> plan,
            IReadOnlyDictionary<SourceKey, TrackedFileWithMetadata> present,
            IReadOnlySet<SourceKey>? outdated)
        {
            HashSet<SourceKey> stale = new();

            if (outdated is not null)
            {
                foreach (SourceKey key in outdated)
                {
                    if (present.ContainsKey(key)) { stale.Add(key); }
                }
            }

            foreach (PlannedSource planned in plan)
            {
                if (!present.TryGetValue(planned.Key, out var file)) { continue; }

                if (stale.Contains(planned.Key)) { continue; }

                foreach (SourceKey dependency in planned.DependsOn)
                {
                    if (!present.TryGetValue(dependency, out var parent))
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

        internal static SourceKey KeyOf(SourceMetadata metadata) =>
            new(metadata.Language, metadata.Type, metadata.Rendition);
    }
}
