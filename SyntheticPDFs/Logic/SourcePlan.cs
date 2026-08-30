namespace SyntheticPDFs.Logic
{
    using RootName = String;

    // identifies one file belonging to a root, without naming it. the three axes are
    // independent: "the Polish parallel text of the worked solutions" is a language,
    // a type and a rendition, and none of them implies the others
    internal readonly record struct SourceKey(
        ISO639_3Code Language,
        SourceType Type,
        SourceRendition Rendition);

    // one file the pipeline is allowed to have for a root, and what it is derived from
    internal record PlannedSource
    {
        internal required SourceKey Key { get; init; }

        // every file that must exist, and be no older than this one, for it to be current
        internal required IReadOnlyList<SourceKey> DependsOn { get; init; }

        // written by a person, so the pipeline maintains it but never creates it
        internal bool Written { get; init; }

        // created without being asked for. everything else is created on request only,
        // and once removed as stale is not rebuilt
        internal bool Eager { get; init; }
    }

    // The single place that knows which files a root may have and what each is derived
    // from. Staleness and batch selection are both read off this, so an archetype rule
    // is stated once - a poster has no worked solutions, a deck of slides reveals its
    // own answers and so needs no separate key - rather than being spread across a
    // staleness check and three batch-selection methods that have to agree.
    internal static class SourcePlan
    {
        private static readonly SourceKey EnglishRoot =
            new(ISO639_3Code.eng, SourceType.Root, SourceRendition.Original);

        private static readonly SourceKey EnglishVocab =
            new(ISO639_3Code.eng, SourceType.Root, SourceRendition.VocabKey);

        // which types an archetype has in English, in the order they are derived
        internal static IReadOnlyList<SourceType> TypesFor(SourceArchetype archetype)
        {
            switch (archetype)
            {
                case SourceArchetype.Worksheet:
                    return new[] { SourceType.Root, SourceType.WorkedSolutions, SourceType.Solutions };

                // the deck reveals its answers on the next overlay, so a separate
                // answer key would be redundant
                case SourceArchetype.QuestionSlides:
                    return new[] { SourceType.Root, SourceType.WorkedSolutions };

                // a poster is a reference sheet - there is nothing to answer
                case SourceArchetype.Poster:
                    return new[] { SourceType.Root };

                default:
                    // only fires if a new archetype is added without deciding its logic
                    throw new NotImplementedException(
                        $"no plan for {archetype} - decide which files it may have");
            }
        }

        internal static IReadOnlyList<PlannedSource> EnglishChain(SourceArchetype archetype) =>
            OriginalChain(archetype, ISO639_3Code.eng);

        // the chain of original files on its own, which is what a CausalFileProcession
        // holds. the language is a parameter only so that a procession can be judged in
        // its own terms; nothing but English has originals
        internal static IReadOnlyList<PlannedSource> OriginalChain(
            SourceArchetype archetype, ISO639_3Code language)
        {
            List<PlannedSource> plan = new();

            List<SourceKey> derivedFrom = new();

            foreach (SourceType type in TypesFor(archetype))
            {
                SourceKey key = new(language, type, SourceRendition.Original);

                plan.Add(new PlannedSource
                {
                    Key        = key,
                    DependsOn  = derivedFrom.ToList(),
                    Written    = type == SourceType.Root,
                    Eager      = true,
                });

                // each step is derived from everything before it, so editing the root
                // invalidates the whole chain below it rather than just the next link
                derivedFrom.Add(key);
            }

            return plan;
        }

        // the whole plan for one root: the English chain, the vocabulary key derived
        // from all of it, and every translated form of it the languages allow
        internal static IReadOnlyList<PlannedSource> For(
            SourceArchetype archetype,
            LanguageTable languages,
            bool includeVocabulary = true)
        {
            List<PlannedSource> plan = new(EnglishChain(archetype));

            if (!includeVocabulary) { return plan; }

            IReadOnlyList<SourceType> types = TypesFor(archetype);

            // the key names the tier 3 words used anywhere in the sheet, so it cannot be
            // written until every part of the sheet exists - but only the parts this
            // archetype actually has, which is why it reads TypesFor rather than
            // assuming worked solutions and answers
            List<SourceKey> wholeSheet = types
                .Select(t => new SourceKey(ISO639_3Code.eng, t, SourceRendition.Original))
                .ToList();

            plan.Add(new PlannedSource
            {
                Key       = EnglishVocab,
                DependsOn = wholeSheet,
                Eager     = true,
            });

            foreach (ISO639_3Code language in languages.All)
            {
                bool eagerLanguage = languages.EagerLanguages.Contains(language);

                SourceKey key = new(language, SourceType.Root, SourceRendition.L2Key);

                plan.Add(new PlannedSource
                {
                    Key       = key,
                    DependsOn = new[] { EnglishVocab },

                    // the key is the seed for everything else in this language, so an
                    // eager language needs it even though nothing prints it directly
                    Eager     = eagerLanguage,
                });

                foreach (SourceType type in types)
                {
                    SourceKey english = new(ISO639_3Code.eng, type, SourceRendition.Original);

                    foreach (SourceRendition rendition in
                        new[] { SourceRendition.ParallelText, SourceRendition.Tier3Only })
                    {
                        plan.Add(new PlannedSource
                        {
                            Key       = new SourceKey(language, type, rendition),
                            DependsOn = new[] { key, english },

                            // only the sheet itself is made without being asked. the
                            // translated worked solutions and answers are generated on
                            // request, since eagerly making all of them for every
                            // language costs far more than it is worth
                            Eager     = eagerLanguage && type == SourceType.Root,
                        });
                    }
                }
            }

            return plan;
        }
    }
}
