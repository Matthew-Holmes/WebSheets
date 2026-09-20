using Shared;

namespace WebSheets.Models
{
    // How many files of one kind exist, out of how many could.
    public readonly record struct CoverageCount(int Made, int Possible)
    {
        // false where nothing of this kind could exist at all - a glossary belongs to a
        // sheet as a whole, so asking for the glossaries of the worked solutions is
        // asking about nothing rather than about none
        public bool Applies => Possible > 0;

        public bool Complete => Possible > 0 && Made == Possible;

        // Rounded, but never rounded past the two answers a reader would check against
        // the listing: one file out of three hundred is not 0%, and one still missing
        // out of three hundred is not 100%. Everything between is rounded as normal.
        public int Percent
        {
            get
            {
                if (Possible == 0 || Made == 0) { return 0; }

                if (Made == Possible) { return 100; }

                return Math.Clamp((int)Math.Round(100d * Made / Possible), 1, 99);
            }
        }

        public static CoverageCount operator +(CoverageCount a, CoverageCount b) =>
            new(a.Made + b.Made, a.Possible + b.Possible);
    }

    // Which part of a sheet the reader is asking about. Worth asking, because only the
    // sheets themselves are translated without being asked for: the worked solutions and
    // the answers are made on request, so counting them all together would show every
    // language as a third of the way there and say nothing about which.
    public enum CoverageScope
    {
        Sheet,
        WorkedSolutions,
        Answers,
        Everything,
    }

    // One language's row: how much of it exists, by part and by form.
    public sealed class LanguageCoverage
    {
        public required LanguageInfo Language { get; init; }

        // The shared dictionary is one file for the whole repository rather than one per
        // sheet, so it is a yes or a no rather than a percentage.
        public required bool HasDictionary { get; init; }

        internal Dictionary<(SheetPart Part, SheetForm Form), CoverageCount> Counts { get; } = new();

        public CoverageCount In(CoverageScope scope, SheetForm form) =>
            TranslationCoverage.PartsOf(scope)
                .Aggregate(default(CoverageCount),
                    (running, part) => running + Counts.GetValueOrDefault((part, form)));

        public CoverageCount Total(CoverageScope scope) =>
            TranslationCoverage.Forms
                .Aggregate(default(CoverageCount),
                    (running, form) => running + In(scope, form));

        // whether this language has anything at all, in any part or form. what separates
        // a language being worked on from one nobody has asked for yet
        public bool Anything =>
            HasDictionary || Counts.Values.Any(c => c.Made > 0);
    }

    // What exists against what could, for every language the generator offers.
    //
    // "Could exist" is not this page's own idea of the rules - it is the same
    // WorksheetGroup.TranslatableForms the EAL page offers a reader, which reads which
    // English files are actually there. So a deck of starters, which has no answers, is
    // never counted as missing their translation, and the percentage here means exactly
    // what the greyed out entries on that page mean.
    public sealed class TranslationCoverage
    {
        public required IReadOnlyList<LanguageCoverage> Languages { get; init; }

        // how many sheets the percentages are out of, for a reader who wants to know
        // whether 80% is eight files or eight hundred
        public required int Sheets { get; init; }

        public CoverageCount Total(CoverageScope scope) =>
            Languages.Aggregate(default(CoverageCount), (running, l) => running + l.Total(scope));

        public int LanguagesWithSomething => Languages.Count(l => l.Anything);

        public int DictionariesMade => Languages.Count(l => l.HasDictionary);

        // The translated forms, in the order they are derived: the key seeds both of the
        // others, which is also the order a reader meets them on the EAL page.
        public static readonly IReadOnlyList<SheetForm> Forms = new[]
        {
            SheetForm.LanguageKey,
            SheetForm.ParallelText,
            SheetForm.Tier3Only,
        };

        public static IReadOnlyList<SheetPart> PartsOf(CoverageScope scope) => scope switch
        {
            CoverageScope.Sheet           => new[] { SheetPart.Sheet },
            CoverageScope.WorkedSolutions => new[] { SheetPart.WorkedSolutions },
            CoverageScope.Answers         => new[] { SheetPart.Solutions },

            _ => new[] { SheetPart.Sheet, SheetPart.WorkedSolutions, SheetPart.Solutions },
        };

        public static TranslationCoverage Of(
            IEnumerable<WorksheetGroup> groups,
            IEnumerable<WorksheetFile> dictionaries,
            IReadOnlyList<LanguageInfo> languages)
        {
            List<WorksheetFile> theDictionary = dictionaries.ToList();

            HashSet<string> withDictionary = theDictionary
                .Select(d => d.LanguageCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // The shared definitions read as a sheet in the listing - they are an
            // English file with translations below them, like everything else - but they
            // are not one. They have no glossary, because they are one, and so no
            // parallel text or key words version either. Counting them would leave every
            // language three files short of a total it can never reach.
            //
            // Which root that is comes from the translations of it rather than from a
            // name written in here: a repository could put its dictionary anywhere.
            HashSet<string> notASheet = theDictionary
                .Select(d => d.RootName)
                .ToHashSet(StringComparer.Ordinal);

            // a group with no English sheet is the leftovers of one that was deleted.
            // nothing more will ever be derived from it, so counting it would hold every
            // language short of a total it cannot reach either
            List<WorksheetGroup> counted = groups
                .Where(g => g.HasSheet && !notASheet.Contains(g.RootName))
                .ToList();

            List<LanguageCoverage> rows = new();

            foreach (LanguageInfo language in languages)
            {
                LanguageCoverage row = new()
                {
                    Language      = language,
                    HasDictionary = withDictionary.Contains(language.Code),
                };

                foreach (WorksheetGroup group in counted)
                {
                    foreach ((SheetPart part, SheetForm form) in group.TranslatableForms())
                    {
                        bool made = group.Translated(language.Code, part, form) is not null;

                        row.Counts[(part, form)] =
                            row.Counts.GetValueOrDefault((part, form))
                            + new CoverageCount(made ? 1 : 0, 1);
                    }
                }

                rows.Add(row);
            }

            return new TranslationCoverage { Languages = rows, Sheets = counted.Count };
        }
    }
}
