using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        // Renditions the generator can actually produce. The plan describes the whole
        // space so that staleness covers it, but asking for a file nothing knows how to
        // write would fail the whole batch, so selection is held to what is implemented.
        private static readonly HashSet<SourceRendition> ImplementedRenditions = new()
        {
            SourceRendition.Original,
            SourceRendition.VocabKey,
            SourceRendition.L2Key,
            SourceRendition.ParallelText,
            SourceRendition.Tier3Only,
        };

        // Gathers everything the whole repository could generate now, then lets the
        // priority gate decide which kind of work this pass does. Nothing is ready until
        // everything it derives from is settled, so a root advances one step per pass on
        // its own without needing to be told to.
        private List<GenerationRequest> GetCreationBatch(
            Dictionary<RootName, RootPlanState> states, int maxBatch)
        {
            List<Candidate> candidates = new();

            int rootOrder = 0;

            foreach (var kvp in states)
            {
                candidates.AddRange(CandidatesFor(kvp.Value, rootOrder));

                rootOrder++;
            }

            return Select(candidates, maxBatch);
        }

        private IEnumerable<Candidate> CandidatesFor(RootPlanState state, int rootOrder)
        {
            if (state.StaleFiles.Count > 0)
            {
                // these should have been removed first!
                throw new ArgumentException("can't generate files while stale files exist!");
            }

            List<Candidate> ret = new();

            long order = 0;

            foreach (PlannedSource planned in state.Creatable())
            {
                if (!ImplementedRenditions.Contains(planned.Key.Rendition)) { continue; }

                long? requestedAt = RequestedAt(state.Root, planned.Key);

                // anything not eager is made only when it has been asked for, and once
                // removed as stale is not rebuilt
                if (!planned.Eager && requestedAt is null) { continue; }

                ret.Add(new Candidate
                {
                    Request   = Request(state.Root, planned.Key, state.Archetype),
                    Priority  = requestedAt is null
                        ? PriorityOf(planned.Key)
                        : GenerationPriority.Requested,
                    RootOrder = rootOrder,
                    Sequence  = requestedAt ?? order++,
                });
            }

            if (ret.Count > 0) { return ret; }

            // work owed on a file that already exists counts as English work, since it is
            // the English deck being settled
            return OutstandingChecks(state).Select(request => new Candidate
            {
                Request   = request,
                Priority  = GenerationPriority.English,
                RootOrder = rootOrder,
                Sequence  = 0,
            });
        }

        // Work owed on a file that already exists, which the plan has nothing to say
        // about - it describes which files there should be, not what has been done to
        // them. Only slides have any: a deck reveals its own answers, so its worked
        // solutions carry a record that the deck was checked, and one without that
        // record still owes the check.
        private List<GenerationRequest> OutstandingChecks(RootPlanState state)
        {
            if (state.Archetype != SourceArchetype.QuestionSlides)
            {
                return new List<GenerationRequest>();
            }

            SourceKey worked = new(
                ISO639_3Code.eng, SourceType.WorkedSolutions, SourceRendition.Original);

            TrackedFileWithMetadata? file = state.File(worked);

            if (file is null) { return new List<GenerationRequest>(); }

            if (WorkedSolutionsRecordAnAnswerMacroCheck(file))
            {
                return new List<GenerationRequest>();
            }

            // no record of a check on these worked solutions - either a human wrote them, or
            // the macros were taken back out of the deck since, so go and look
            return new List<GenerationRequest>
            {
                Request(state.Root, worked, state.Archetype, GenerationJob.CheckAnswerMacros)
            };
        }

        // every request is for one file belonging to one root, so this saves spelling
        // the metadata out at each of the call sites
        private static GenerationRequest Request(
            RootName root,
            SourceKey key,
            SourceArchetype at,
            GenerationJob job = GenerationJob.CreateSource)
        {
            return new GenerationRequest
            {
                Target = new SourceMetadata
                {
                    RootName  = root,
                    Language  = key.Language,
                    Type      = key.Type,
                    Rendition = key.Rendition,
                    Archetype = at,
                },
                Job = job,
            };
        }

        // the record is a line inside the worked solutions, so this has to read the file.
        // that is disk, not network, and it stops the check being paid for every pass
        private bool WorkedSolutionsRecordAnAnswerMacroCheck(TrackedFileWithMetadata worked)
        {
            String filename = worked.TrackedFile.FullPath;

            try
            {
                return AnswerMacros.IsMarkedVerified(RepoManager.GetContent(filename).TexSource);
            }
            catch (Exception e)
            {
                // one unreadable file mustn't stop the pass, and since we can't tell whether
                // a check is owed, the cheap answer is the one that spends nothing
                _logger.LogWarning(
                    "could not read {File} to look for the answer macro marker: {Message}",
                    filename, e.Message);

                return true;
            }
        }
    }
}
