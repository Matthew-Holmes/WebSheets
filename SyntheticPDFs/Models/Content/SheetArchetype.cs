namespace SyntheticPDFs.Models.Content
{
    // What kind of thing a source file is, and everything that follows from that.
    //
    // One class per archetype, so that adding a kind of source is a matter of writing a
    // file rather than of finding every switch that has an opinion about it. What used
    // to be an enum read in five places is now a set of virtual properties read off the
    // instance: which parts it has, whether it is translated, how it is named, and what
    // its plan looks like.
    //
    // Instances are singletons handed out by SheetArchetypes, so they compare by
    // reference and can sit inside a record without spoiling its equality.
    internal abstract class SheetArchetype
    {
        // The class name is the archetype's name everywhere - in a log line, in a test
        // row, and in the warning printed when a folder matches none of them. Read off
        // the type so a name cannot drift out of step with the class it belongs to.
        internal String Name => GetType().Name;

        // What to call one of these in a sentence a person reads.
        internal abstract String Description { get; }

        // The folder under the source directory that holds this kind of file, and the
        // whole of the convention that decides what a file is: anything under
        // "latex/starters/" is a deck of question slides, however it is named.
        internal abstract String Folder { get; }

        // Which parts one of these has, in the order they are derived. Each is derived
        // from every part before it, so this is a chain rather than a set.
        internal abstract IReadOnlyList<SheetPart> Parts { get; }

        internal bool Has(SheetPart part) => Parts.Contains(part);

        internal bool HasWorkedSolutions => Has(SheetPart.WorkedSolutions);

        internal bool HasSolutions => Has(SheetPart.Solutions);

        // Whether the tier 3 glossary is written for it. True of every kind of sheet -
        // even a poster, which is dense with the subject's own words - and false only of
        // the dictionary, which has no glossary because it is one.
        internal virtual bool HasGlossary => true;

        // A deck reveals its own answers on its next overlay, which means the deck has to
        // define and use the helpers that do it. That is work owed against a file that
        // already exists, so no plan can express it - it is asked about separately.
        internal virtual bool RevealsItsOwnAnswers => false;

        // Extra English versions of this kind of source: the same file with something
        // about it changed for a school that wants it differently, made from the file
        // itself rather than written again.
        //
        // Empty for everything but a deck of starters, and named here so that adding a
        // variant is a form, a rewriter and one line on the archetype that wants it -
        // nothing has to learn that a variant exists in order to plan, judge or name it.
        internal virtual IReadOnlyList<SheetForm> Variants { get; } =
            Array.Empty<SheetForm>();

        // Extra instructions the worked-solutions prompt needs for this kind of source.
        // Named here rather than chosen by a switch elsewhere, so that adding an
        // archetype is one file; the wording itself lives with the other prompts.
        internal virtual String? WorkedSolutionsInstructions => null;

        #region Its naming convention in the repository

        // The path this file takes, minus the extension. Overridden by an archetype that
        // is not one-file-per-sheet - the dictionary is the whole repository's, not a
        // particular sheet's, and is named accordingly.
        internal virtual String FileNameFor(SourceMetadata metadata) =>
            ContentNaming.StandardName(metadata);

        internal virtual SourceMetadata Parse(String pathNoExt, ILogger? logger = null) =>
            ContentNaming.ParseStandard(this, pathNoExt, logger);

        #endregion

        #region Its plan

        // The chain of English originals on its own - the questions, the workings derived
        // from them, and the answers derived from those.
        //
        // The language is a parameter only so that a chain can be judged in its own
        // terms; nothing but English has originals.
        internal IReadOnlyList<PlannedFile> OriginalChain(ISO639_3Code language)
        {
            List<PlannedFile> plan = new();

            List<ContentKey> derivedFrom = new();

            foreach (SheetPart part in Parts)
            {
                ContentKey key = new(language, part, SheetForm.Original);

                plan.Add(new PlannedFile
                {
                    Key       = key,
                    DependsOn = derivedFrom.ToList(),
                    Written   = part == SheetPart.Root,
                    Eager     = true,
                });

                // each step is derived from everything before it, so editing the root
                // invalidates the whole chain below it rather than just the next link
                derivedFrom.Add(key);
            }

            return plan;
        }

        internal IReadOnlyList<PlannedFile> EnglishChain() => OriginalChain(ISO639_3Code.eng);

        // The whole plan for one of these: the English chain, the glossary derived from
        // all of it, and every translated version of it the languages allow.
        internal virtual IReadOnlyList<PlannedFile> Plan(
            LanguageTable languages, bool includeGlossaries = true)
        {
            List<PlannedFile> plan = new(EnglishChain());

            // Each variant is made from the part it is a variant of and from nothing
            // else, so it can be written the moment that part exists and it goes stale
            // the moment that part is edited. There are no translated variants: a
            // translation is printed for a handful of pupils, where the wording on the
            // board is not what is being read.
            foreach (SheetForm variant in Variants)
            {
                foreach (SheetPart part in Parts)
                {
                    ContentKey original = new(ISO639_3Code.eng, part, SheetForm.Original);

                    plan.Add(new PlannedFile
                    {
                        Key       = new ContentKey(ISO639_3Code.eng, part, variant),
                        DependsOn = new[] { original },
                        Eager     = true,
                    });
                }
            }

            if (!includeGlossaries || !HasGlossary) { return plan; }

            ContentKey glossary = new(ISO639_3Code.eng, SheetPart.Root, SheetForm.Glossary);

            // the glossary names the tier 3 words used anywhere in the sheet, so it
            // cannot be written until every part of the sheet exists - but only the parts
            // this archetype actually has, which is why it reads Parts rather than
            // assuming worked solutions and answers
            plan.Add(new PlannedFile
            {
                Key       = glossary,
                DependsOn = Parts
                    .Select(p => new ContentKey(ISO639_3Code.eng, p, SheetForm.Original))
                    .ToList(),
                Eager     = true,
            });

            foreach (ISO639_3Code language in languages.All)
            {
                bool eagerLanguage = languages.EagerLanguages.Contains(language);

                ContentKey translatedGlossary =
                    new(language, SheetPart.Root, SheetForm.TranslatedGlossary);

                plan.Add(new PlannedFile
                {
                    Key       = translatedGlossary,
                    DependsOn = new[] { glossary },

                    // the translated glossary is the seed for everything else in this
                    // language, so an eager language needs it even though nothing prints
                    // it directly
                    Eager     = eagerLanguage,
                });

                foreach (SheetPart part in Parts)
                {
                    ContentKey english = new(ISO639_3Code.eng, part, SheetForm.Original);

                    foreach (SheetForm form in
                        new[] { SheetForm.ParallelText, SheetForm.Tier3Only })
                    {
                        plan.Add(new PlannedFile
                        {
                            Key       = new ContentKey(language, part, form),
                            DependsOn = new[] { translatedGlossary, english },

                            // only the sheet itself is made without being asked. the
                            // translated worked solutions and answers are generated on
                            // request, since eagerly making all of them for every
                            // language costs far more than it is worth
                            Eager     = eagerLanguage && part == SheetPart.Root,
                        });
                    }
                }
            }

            return plan;
        }

        #endregion

        public override String ToString() => Name;
    }
}
