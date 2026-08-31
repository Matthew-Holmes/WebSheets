namespace SyntheticPDFs.Models.Content.Archetypes
{
    // The shared definitions, and their translations.
    //
    // Special in every way an archetype can be, which is why it has a class of its own.
    // There is one of it for the whole repository rather than one per sheet, so it is not
    // named like a sheet; the English one is written by a person and never by the
    // pipeline; it has no glossary because it is one; and its translations are derived
    // from it alone rather than from a sheet's glossary.
    //
    //   latex/dictionary/mathematicalDictionary.tex          written by hand
    //   latex/dictionary/L2/pol/mathematicalDictionary_polish.tex   generated
    //
    // What makes it worth the trouble is that the translations are a cache. A word
    // defined once and translated once is then reused by every sheet that mentions it,
    // instead of being sent to a model again with each sheet that does.
    internal sealed class MathematicalDictionary : SheetArchetype
    {
        internal override String Description => "the shared dictionary";

        internal override String Folder => "dictionary";

        internal override IReadOnlyList<SheetPart> Parts { get; } = new[] { SheetPart.Root };

        // it is the glossary the sheets' glossaries are checked against
        internal override bool HasGlossary => false;

        #region Its naming convention in the repository

        // <folder>/L2/<code>/<basename>_<languageName>, so that every translation of the
        // dictionary sits together under the folder the English one lives in rather than
        // in a folder named after the dictionary itself.
        internal override String FileNameFor(SourceMetadata metadata)
        {
            if (metadata.Language == ISO639_3Code.eng) { return metadata.RootName; }

            String languageName = ContentNaming.NameOfLanguage(metadata.Language);

            int lastSlash = metadata.RootName.LastIndexOf('/');

            String folder = lastSlash < 0 ? String.Empty : metadata.RootName[..lastSlash];
            String basename = metadata.RootName[(lastSlash + 1)..];

            return String.Join('/',
                folder,
                ContentNaming.L2DirectoryName,
                metadata.Language.Code,
                $"{basename}_{languageName}");
        }

        internal override SourceMetadata Parse(String pathNoExt, ILogger? logger = null)
        {
            SourceMetadata metadata = new()
            {
                Part      = SheetPart.Root,
                Archetype = this,
                Language  = ISO639_3Code.eng,
                RootName  = pathNoExt,
            };

            String marker = '/' + ContentNaming.L2DirectoryName + '/';

            int at = pathNoExt.IndexOf(marker, StringComparison.Ordinal);

            if (at < 0) { return metadata; }

            String[] rest = pathNoExt[(at + marker.Length)..].Split('/');

            if (rest.Length != 2)
            {
                logger?.LogWarning(
                    "'{File}' is in the dictionary folder but not in the expected "
                    + "{L2}/<code>/<file> shape - treating it as a dictionary of its own.",
                    pathNoExt, ContentNaming.L2DirectoryName);

                return metadata;
            }

            String code = rest[0];

            String? languageName = LanguageNames.EnglishNameOf(code);

            if (languageName is null)
            {
                logger?.LogWarning(
                    "'{File}' is under {L2}/{Code}/, which is not a language code we recognise - "
                    + "treating it as a dictionary of its own. Add {Code} to LanguageNames if it "
                    + "is one.",
                    pathNoExt, ContentNaming.L2DirectoryName, code, code);

                return metadata;
            }

            String suffix = '_' + languageName;

            if (!rest[1].EndsWith(suffix, StringComparison.Ordinal))
            {
                logger?.LogWarning(
                    "'{File}' is under {L2}/{Code}/ but its name does not end in '{Language}' - "
                    + "treating it as a dictionary of its own.",
                    pathNoExt, ContentNaming.L2DirectoryName, code, languageName);

                return metadata;
            }

            String basename = rest[1][..^suffix.Length];

            return metadata with
            {
                Language = new ISO639_3Code(code),
                Form     = SheetForm.ParallelText,
                RootName = String.Join('/', pathNoExt[..at], basename),
            };
        }

        #endregion

        // The plan holds the English dictionary alone, and its translations are not in it.
        //
        // A plan says which files should exist and rebuilds one from scratch when what it
        // was built from changes. That is the wrong shape for a translated dictionary,
        // which is refreshed instead: a word whose English definition has not changed
        // keeps the translation it already has, and only the difference is sent to a
        // model. Rebuilding one the ordinary way would throw away hundreds of translations
        // to pick up one new word, which is the opposite of what it is for.
        internal override IReadOnlyList<PlannedFile> Plan(
            LanguageTable languages, bool includeGlossaries = true)
        {
            return new[]
            {
                new PlannedFile
                {
                    Key       = new ContentKey(ISO639_3Code.eng, SheetPart.Root, SheetForm.Original),
                    DependsOn = Array.Empty<ContentKey>(),
                    Written   = true,
                    Eager     = true,
                },
            };
        }
    }
}
