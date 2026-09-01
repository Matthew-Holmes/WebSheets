namespace SyntheticPDFs.Models.Content
{
    // The naming convention shared by everything the pipeline writes:
    //
    //   camelCase.tex
    //   camelCase_workedSolutions.tex
    //   camelCase_solutions.tex
    //   camelCase_vocab.tex
    //   camelCase/L2/pol/camelCase_polishKey.tex
    //   camelCase/L2/pol/camelCase_polishParallelText.tex
    //   camelCase/L2/pol/camelCase_workedSolutions_polishTier3Only.tex
    //
    // The language lives in a folder rather than in the name, so that a person reading a
    // served pdf filename sees "polish" rather than an ISO code few people know, and so
    // that a sheet's translations stay together on disk.
    //
    // An archetype that names its files some other way overrides FileNameFor and Parse
    // rather than adding a case here - the dictionary does exactly that, since it is one
    // file for the whole repository and not one per sheet.
    internal static class ContentNaming
    {
        internal const String WorkedSolutionsIndicator = "workedSolutions";
        internal const String SolutionsIndicator = "solutions";
        internal const String GlossaryIndicator = "vocab";

        // A variant's suffix goes on the outside, after the part - so the worked
        // solutions of a starter in one school's wording is
        // "algebraStarters_workedSolutions_retrieveAndConnect". Written that way round
        // so that a name still reads left to right as sheet, part, then variant, and so
        // that adding a variant cannot change how an existing name parses.
        internal const String RetrieveAndConnectIndicator = "retrieveAndConnect";

        // the folder under a root that holds every translation of it
        internal const String L2DirectoryName = "L2";

        // must match ContentRepository:SourceDirectory in appsettings.json
        internal const String SourceDirectoryName = "latex";

        #region Writing a name

        internal static String StandardName(SourceMetadata metadata)
        {
            if (metadata.Language != ISO639_3Code.eng)
            {
                return TranslatedName(metadata);
            }

            var sb = new System.Text.StringBuilder(metadata.RootName);

            AppendPartSuffix(sb, metadata.Part);

            if (metadata.Form == SheetForm.Glossary)
            {
                sb.Append('_').Append(GlossaryIndicator);
            }

            if (metadata.Form == SheetForm.RetrieveAndConnect)
            {
                sb.Append('_').Append(RetrieveAndConnectIndicator);
            }

            return sb.ToString();
        }

        // RootName/L2/<code>/<basename>[_part]_<languageName><Form>
        private static String TranslatedName(SourceMetadata metadata)
        {
            String languageName = NameOfLanguage(metadata.Language);

            String basename = metadata.RootName.Split('/').Last();

            var sb = new System.Text.StringBuilder(basename);

            AppendPartSuffix(sb, metadata.Part);

            sb.Append('_').Append(languageName).Append(FormSuffix(metadata.Form));

            return String.Join(
                '/', metadata.RootName, L2DirectoryName, metadata.Language.Code, sb.ToString());
        }

        internal static String NameOfLanguage(ISO639_3Code language)
        {
            String? name = LanguageNames.EnglishNameOf(language.Code);

            if (name is null)
            {
                throw new ArgumentException(
                    $"'{language.Code}' is not a language we can name - add it to LanguageNames");
            }

            return name;
        }

        internal static void AppendPartSuffix(System.Text.StringBuilder sb, SheetPart part)
        {
            switch (part)
            {
                case SheetPart.Root:
                    break;
                case SheetPart.WorkedSolutions:
                    sb.Append('_').Append(WorkedSolutionsIndicator);
                    break;
                case SheetPart.Solutions:
                    sb.Append('_').Append(SolutionsIndicator);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        internal static String FormSuffix(SheetForm form)
        {
            switch (form)
            {
                case SheetForm.TranslatedGlossary: return "Key";
                case SheetForm.ParallelText:       return "ParallelText";
                case SheetForm.Tier3Only:          return "Tier3Only";
                default:
                    throw new ArgumentException(
                        $"{form} is not a form a translated file can have");
            }
        }

        internal static SheetForm? ParseFormSuffix(String suffix)
        {
            switch (suffix)
            {
                case "Key":          return SheetForm.TranslatedGlossary;
                case "ParallelText": return SheetForm.ParallelText;
                case "Tier3Only":    return SheetForm.Tier3Only;
                default:             return null;
            }
        }

        #endregion

        #region Reading a name back

        // logger is optional so the parse stays static and testable - callers that have
        // one pass it so misnamed files get surfaced rather than silently absorbed
        internal static SourceMetadata ParseStandard(
            SheetArchetype archetype, String filenameNoExt, ILogger? logger)
        {
            // english language defaults
            SourceMetadata metadata = new()
            {
                Part      = SheetPart.Root,
                Archetype = archetype,
                Language  = ISO639_3Code.eng,
                RootName  = filenameNoExt,
            };

            if (filenameNoExt.Contains('/' + L2DirectoryName + '/', StringComparison.Ordinal))
            {
                return ParseTranslated(filenameNoExt, metadata, logger);
            }

            // The variant suffix sits outside the part suffix, so it comes off first and
            // everything below then reads the name as though the variant were not there.
            String name = filenameNoExt;

            String variant = '_' + RetrieveAndConnectIndicator;

            if (name.EndsWith(variant, StringComparison.Ordinal))
            {
                name = name[..^variant.Length];

                metadata = metadata with
                {
                    Form     = SheetForm.RetrieveAndConnect,
                    RootName = name,
                };
            }

            String[] parts = name.Split('_');

            // no suffix at all => an English root, so handle that here
            if (parts.Length == 1) { return metadata; }

            String rootName = String.Join('_', parts.Take(parts.Length - 1));

            if (parts.Last() == WorkedSolutionsIndicator)
            {
                return metadata with { Part = SheetPart.WorkedSolutions, RootName = rootName };
            }

            if (parts.Last() == SolutionsIndicator)
            {
                return metadata with { Part = SheetPart.Solutions, RootName = rootName };
            }

            if (parts.Last() == GlossaryIndicator)
            {
                return metadata with { Form = SheetForm.Glossary, RootName = rootName };
            }

            String isoCodeMaybe = parts.Last();

            if (isoCodeMaybe.Length != 3 || !LanguageNames.IsKnown(isoCodeMaybe))
            {
                return metadata;
            }

            // a name ending in a language code we recognise. translations live in an L2
            // folder now, so this is either an old-style name or a coincidence - either
            // way the whole name is the root, but it is worth saying so
            logger?.LogWarning(
                "'{File}' ends in '_{Suffix}', which is a language code - translations live in "
                + "an {L2}/<code>/ folder now, so this is being treated as an English root. "
                + "Rename it if it was meant to be a translation.",
                filenameNoExt, isoCodeMaybe, L2DirectoryName);

            return metadata;
        }

        // RootName/L2/<code>/<basename>[_part]_<languageName><Form>
        private static SourceMetadata ParseTranslated(
            String filenameNoExt, SourceMetadata metadata, ILogger? logger)
        {
            String marker = '/' + L2DirectoryName + '/';

            int at = filenameNoExt.IndexOf(marker, StringComparison.Ordinal);

            String rootName = filenameNoExt[..at];

            String[] rest = filenameNoExt[(at + marker.Length)..].Split('/');

            if (rest.Length != 2)
            {
                logger?.LogWarning(
                    "'{File}' has an {L2} folder but not the expected {L2}/<code>/<file> shape - "
                    + "treating it as an English root.",
                    filenameNoExt, L2DirectoryName, L2DirectoryName);

                return metadata;
            }

            String code = rest[0];
            String filename = rest[1];

            String? languageName = LanguageNames.EnglishNameOf(code);

            if (languageName is null)
            {
                logger?.LogWarning(
                    "'{File}' is under {L2}/{Code}/, which is not a language code we recognise - "
                    + "treating it as an English root. Add {Code} to LanguageNames if it is one.",
                    filenameNoExt, L2DirectoryName, code, code);

                return metadata;
            }

            // the form is the tail after the language name, and the part is whatever
            // suffix sits in front of it
            int nameAt = filename.LastIndexOf('_' + languageName, StringComparison.Ordinal);

            if (nameAt < 0)
            {
                logger?.LogWarning(
                    "'{File}' is under {L2}/{Code}/ but its name does not carry '{Language}' - "
                    + "treating it as an English root.",
                    filenameNoExt, L2DirectoryName, code, languageName);

                return metadata;
            }

            String beforeName = filename[..nameAt];
            String formPart = filename[(nameAt + 1 + languageName.Length)..];

            SheetForm? form = ParseFormSuffix(formPart);

            if (form is null)
            {
                logger?.LogWarning(
                    "'{File}' ends in '{Form}', which is not a translated form we produce - "
                    + "treating it as an English root.",
                    filenameNoExt, formPart);

                return metadata;
            }

            SheetPart part = SheetPart.Root;

            if (beforeName.EndsWith('_' + WorkedSolutionsIndicator, StringComparison.Ordinal))
            {
                part = SheetPart.WorkedSolutions;
            }
            else if (beforeName.EndsWith('_' + SolutionsIndicator, StringComparison.Ordinal))
            {
                part = SheetPart.Solutions;
            }

            return metadata with
            {
                RootName = rootName,
                Language = new ISO639_3Code(code),
                Part     = part,
                Form     = (SheetForm)form,
            };
        }

        #endregion
    }
}
