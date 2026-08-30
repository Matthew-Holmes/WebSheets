using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        // What gets done first. English source is finished across the whole repository
        // before any vocabulary is written, and all the vocabulary before any translation,
        // so the repository is always complete in the language most of its readers use.
        //
        // Anything asked for explicitly comes before all of it, in the order it was asked.
        internal enum GenerationPriority
        {
            Requested    = 0,
            English      = 1,
            EnglishVocab = 2,
            TranslatedKey = 3,
            Translated   = 4,
        }

        internal static GenerationPriority PriorityOf(SourceKey key)
        {
            switch (key.Rendition)
            {
                case SourceRendition.Original: return GenerationPriority.English;
                case SourceRendition.VocabKey: return GenerationPriority.EnglishVocab;
                case SourceRendition.L2Key:    return GenerationPriority.TranslatedKey;
                default:                       return GenerationPriority.Translated;
            }
        }

        // one thing that could be generated now, and how urgent it is
        private record Candidate
        {
            internal required GenerationRequest Request { get; init; }
            internal required GenerationPriority Priority { get; init; }

            // where this root came in the repository, so a root is finished before the
            // next is started rather than every root advancing a little
            internal required int RootOrder { get; init; }

            // for requested work, when it was asked for; otherwise the plan's own order
            internal required long Sequence { get; init; }
        }

        // A pass does one kind of work: the most urgent kind there is anything of.
        //
        // A hard gate rather than a sort, because the tiers are not merely preferences.
        // Vocabulary is derived from finished English source, and translations from
        // finished vocabulary, so doing a little of each would mean writing files from
        // parents that are about to change.
        private List<GenerationRequest> Select(List<Candidate> candidates, int maxBatch)
        {
            if (candidates.Count == 0) { return new List<GenerationRequest>(); }

            GenerationPriority most = candidates.Min(c => c.Priority);

            List<Candidate> chosen = candidates
                .Where(c => c.Priority == most)
                .OrderBy(c => c.Sequence)
                .ThenBy(c => c.RootOrder)
                .Take(maxBatch)
                .ToList();

            if (most != GenerationPriority.English)
            {
                _logger.LogInformation(
                    "no {Blocked} work outstanding, so this pass is doing {Doing} work",
                    GenerationPriority.English, most);
            }

            return chosen.Select(c => c.Request).ToList();
        }
    }
}
