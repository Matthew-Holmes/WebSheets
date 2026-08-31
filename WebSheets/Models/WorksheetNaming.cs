using Shared;

namespace WebSheets.Models
{
    // What a worksheet file is, read from its name.
    //
    // These mirror the naming the generator writes (SyntheticPDFs, Orchestrator's
    // naming conventions). The site only ever reads names, never builds the source
    // ones, so a small reader here is a fairer trade than sharing the generator's
    // internals - but if the convention changes, this is the file that has to follow.
    public enum SheetPart
    {
        Sheet,           // the questions themselves
        WorkedSolutions,
        Solutions,
    }

    public enum SheetForm
    {
        Original,     // the English file as written or derived
        Glossary,     // the tier 3 vocabulary key for the sheet
        LanguageKey,  // that key translated
        ParallelText, // the whole text translated above the English
        Tier3Only,    // only the tier 3 words glossed
        Dictionary,   // the shared definitions in one language
    }

    public record WorksheetFile
    {
        // the sheet this belongs to, without any suffix - "circlesAreaIdeasStarters"
        public required string RootName { get; init; }

        public required SheetPart Part { get; init; }

        public required SheetForm Form { get; init; }

        // empty for English files
        public string LanguageCode { get; init; } = "";

        // the file as the object store holds it, hash and all
        public required string FileName { get; init; }

        // full path within the store, for building a download link
        public required string FullPath { get; init; }

        public bool IsTranslated => LanguageCode.Length > 0;
    }

    /// <summary>
    /// Shared naming convention for generated worksheet files: each file name
    /// (before its extension) ends with a git-hash suffix - a 12 character hash
    /// plus the separating underscore - appended by the content-generation
    /// pipeline (see the SyntheticPDFs branch). This is the single place that
    /// knows how to strip that suffix, so callers don't re-implement it with
    /// their own magic numbers.
    /// </summary>
    public static class WorksheetNaming
    {
        public const int GitHashLength = 12;
        public const int HashSuffixLength = GitHashLength + 1; // plus the separating underscore

        // the folder a sheet's translations live in, below the sheet's own name
        public const string TranslationFolder = "L2";

        private const string WorkedSolutionsIndicator = "workedSolutions";
        private const string SolutionsIndicator = "solutions";
        private const string GlossaryIndicator = "vocab";

        /// <summary>
        /// Removes the trailing git-hash suffix from a name that has already
        /// had its extension removed. Returns the input unchanged if it isn't
        /// long enough to contain the suffix.
        /// </summary>
        public static string StripHashSuffix(string nameWithoutExtension)
        {
            if (string.IsNullOrEmpty(nameWithoutExtension) || nameWithoutExtension.Length <= HashSuffixLength)
                return nameWithoutExtension;

            return nameWithoutExtension[..^HashSuffixLength];
        }

        // Reads one file in the store. The language, when there is one, comes from the
        // folder the file sits in rather than from its name, so the name can stay
        // readable - "circlesAreaIdeasStarters_polishParallelText" rather than an ISO
        // code few people would recognise.
        public static WorksheetFile? Parse(
            string fileName, string directoryPath, IReadOnlyList<LanguageInfo> languages)
        {
            if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { return null; }

            string bare = StripHashSuffix(fileName[..^".pdf".Length]);

            string fullPath = directoryPath.Length == 0 ? fileName : $"{directoryPath}/{fileName}";

            string languageCode = LanguageOf(directoryPath);

            if (languageCode.Length == 0)
            {
                return ParseEnglish(bare, fileName, fullPath);
            }

            LanguageInfo? language = languages
                .FirstOrDefault(l => string.Equals(l.Code, languageCode, StringComparison.OrdinalIgnoreCase));

            // a translation into something no longer offered - readable enough to list,
            // but there is no language to name it by
            if (language is null) { return null; }

            return ParseTranslated(bare, fileName, fullPath, language);
        }

        // ".../circlesAreaIdeasStarters/L2/pol" -> "pol"
        public static string LanguageOf(string directoryPath)
        {
            string[] parts = directoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i + 1 < parts.Length; i++)
            {
                if (parts[i] == TranslationFolder) { return parts[i + 1]; }
            }

            return "";
        }

        private static WorksheetFile? ParseEnglish(string bare, string fileName, string fullPath)
        {
            (string root, SheetPart part) = SplitPart(bare);

            SheetForm form = SheetForm.Original;

            if (root.EndsWith('_' + GlossaryIndicator, StringComparison.Ordinal))
            {
                root = root[..^(GlossaryIndicator.Length + 1)];
                form = SheetForm.Glossary;
            }

            return new WorksheetFile
            {
                RootName = root,
                Part     = part,
                Form     = form,
                FileName = fileName,
                FullPath = fullPath,
            };
        }

        // "<root>[_part]_<languageName><Form>"
        private static WorksheetFile? ParseTranslated(
            string bare, string fileName, string fullPath, LanguageInfo language)
        {
            string suffix = '_' + language.Name.ToLowerInvariant();

            int at = bare.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);

            if (at < 0) { return null; }

            SheetForm? form = bare[(at + suffix.Length)..] switch
            {
                "Key"          => SheetForm.LanguageKey,
                "ParallelText" => SheetForm.ParallelText,
                "Tier3Only"    => SheetForm.Tier3Only,

                // nothing at all after the language name is the shared dictionary in
                // that language - the one generated file that belongs to the whole
                // repository rather than to a sheet, and so the one with no form to name
                ""             => SheetForm.Dictionary,

                _              => null,
            };

            if (form is null) { return null; }

            (string root, SheetPart part) = SplitPart(bare[..at]);

            return new WorksheetFile
            {
                RootName     = root,
                Part         = part,
                Form         = (SheetForm)form,
                LanguageCode = language.Code,
                FileName     = fileName,
                FullPath     = fullPath,
            };
        }

        private static (string Root, SheetPart Part) SplitPart(string bare)
        {
            if (bare.EndsWith('_' + WorkedSolutionsIndicator, StringComparison.Ordinal))
            {
                return (bare[..^(WorkedSolutionsIndicator.Length + 1)], SheetPart.WorkedSolutions);
            }

            if (bare.EndsWith('_' + SolutionsIndicator, StringComparison.Ordinal))
            {
                return (bare[..^(SolutionsIndicator.Length + 1)], SheetPart.Solutions);
            }

            return (bare, SheetPart.Sheet);
        }

        // The name the generator would give a file that does not exist yet, so the site
        // can show what is missing rather than quietly leaving it out. No hash: it has
        // never been built, so there is no commit to name it after.
        public static string TranslatedName(
            string rootName, SheetPart part, SheetForm form, LanguageInfo language)
        {
            string partSuffix = part switch
            {
                SheetPart.WorkedSolutions => '_' + WorkedSolutionsIndicator,
                SheetPart.Solutions       => '_' + SolutionsIndicator,
                _                         => "",
            };

            string formSuffix = form switch
            {
                SheetForm.LanguageKey  => "Key",
                SheetForm.ParallelText => "ParallelText",
                SheetForm.Tier3Only    => "Tier3Only",
                _                      => "",
            };

            return $"{rootName}{partSuffix}_{language.Name.ToLowerInvariant()}{formSuffix}";
        }

        // What a dictionary is called in a listing. On disk it is named for the pipeline
        // - "mathematicalDictionary_polish" - and nobody looking for the Polish
        // definitions would think to look under an English word they cannot read.
        public static string DictionaryTitle(LanguageInfo language) =>
            $"{language.Name} Dictionary";

        // how a form reads in a menu
        public static string Describe(SheetPart part, SheetForm form)
        {
            string what = form switch
            {
                SheetForm.Glossary     => "glossary",
                SheetForm.LanguageKey  => "glossary",
                SheetForm.ParallelText => "parallel text",
                SheetForm.Tier3Only    => "key words only",
                SheetForm.Dictionary   => "dictionary",
                _                      => "sheet",
            };

            if (form is SheetForm.Glossary or SheetForm.LanguageKey or SheetForm.Dictionary)
            {
                return what;
            }

            string of = part switch
            {
                SheetPart.WorkedSolutions => "worked solutions",
                SheetPart.Solutions       => "answers",
                _                         => "the sheet",
            };

            return form == SheetForm.Original ? of : $"{of}: {what}";
        }
    }
}
