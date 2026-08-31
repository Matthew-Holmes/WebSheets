using Shared;

namespace WebSheets.Models
{
    // A sheet and everything derived from it, as one entry in a listing.
    //
    // The listing used to give every file a bullet of its own, which meant a folder of
    // eight worksheets read as twenty-four lines with the same names three times over.
    // Grouping them puts the sheet forward and leaves the rest to a menu.
    public class WorksheetGroup
    {
        public required string RootName { get; init; }

        // the English files, by which part of the sheet they are
        public Dictionary<SheetPart, WorksheetFile> Parts { get; } = new();

        public WorksheetFile? Glossary { get; set; }

        // language code -> the translated files for it
        public Dictionary<string, List<WorksheetFile>> Translations { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public WorksheetFile? Sheet => Parts.GetValueOrDefault(SheetPart.Sheet);

        public WorksheetFile? WorkedSolutions => Parts.GetValueOrDefault(SheetPart.WorkedSolutions);

        public WorksheetFile? Solutions => Parts.GetValueOrDefault(SheetPart.Solutions);

        // a group with no sheet is a leftover - the derived files of something deleted.
        // it is still worth listing, so that it can be seen and cleared up
        public bool HasSheet => Sheet is not null;

        public bool HasAnythingBeyondTheSheet =>
            WorkedSolutions is not null || Solutions is not null
            || Glossary is not null || Translations.Count > 0;

        // Builds the groups for one directory. Translations live in a folder below the
        // sheet rather than beside it, so they are passed in separately.
        public static List<WorksheetGroup> Build(
            IEnumerable<WorksheetFile> here,
            IEnumerable<WorksheetFile> translations)
        {
            Dictionary<string, WorksheetGroup> groups = new(StringComparer.Ordinal);

            WorksheetGroup For(string root)
            {
                if (!groups.TryGetValue(root, out WorksheetGroup? group))
                {
                    group = new WorksheetGroup { RootName = root };
                    groups[root] = group;
                }

                return group;
            }

            foreach (WorksheetFile file in here)
            {
                WorksheetGroup group = For(file.RootName);

                if (file.Form == SheetForm.Glossary)
                {
                    group.Glossary = file;
                    continue;
                }

                group.Parts[file.Part] = file;
            }

            foreach (WorksheetFile file in translations)
            {
                WorksheetGroup group = For(file.RootName);

                if (!group.Translations.TryGetValue(file.LanguageCode, out var forLanguage))
                {
                    forLanguage = new List<WorksheetFile>();
                    group.Translations[file.LanguageCode] = forLanguage;
                }

                forLanguage.Add(file);
            }

            return groups.Values
                .OrderBy(g => g.RootName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Which translated forms are worth offering for this sheet. A poster has no
        // answers and a deck of slides no answer key, so offering to translate them
        // would invite a request that can never be satisfied. Rather than repeat the
        // generator's rules, this reads what English files actually exist.
        public IEnumerable<(SheetPart Part, SheetForm Form)> TranslatableForms()
        {
            yield return (SheetPart.Sheet, SheetForm.LanguageKey);

            foreach (SheetPart part in new[]
                { SheetPart.Sheet, SheetPart.WorkedSolutions, SheetPart.Solutions })
            {
                if (!Parts.ContainsKey(part)) { continue; }

                yield return (part, SheetForm.ParallelText);
                yield return (part, SheetForm.Tier3Only);
            }
        }

        public WorksheetFile? Translated(string languageCode, SheetPart part, SheetForm form)
        {
            if (!Translations.TryGetValue(languageCode, out var files)) { return null; }

            return files.FirstOrDefault(f => f.Part == part && f.Form == form);
        }

        public bool HasAnyTranslationIn(string languageCode) =>
            Translations.TryGetValue(languageCode, out var files) && files.Count > 0;

        public IEnumerable<LanguageInfo> LanguagesPresent(IReadOnlyList<LanguageInfo> known) =>
            known.Where(l => HasAnyTranslationIn(l.Code));
    }
}
