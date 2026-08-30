using System.Text;

namespace SyntheticPDFs.Logic
{
    internal enum SourceType
    {
        Root,
        WorkedSolutions,
        Solutions,
    }

    // which version of a file this is. the type says whether it is the questions, the
    // workings or the answers; the rendition says whether it is the English original,
    // the vocabulary key derived from it, or one of the translated forms
    internal enum SourceRendition
    {
        Original,     // English only - the file as written or derived
        VocabKey,     // English only, Root - the tier 3 key for the whole sheet
        L2Key,        // translated only, Root - the translation of that key
        ParallelText, // translated only - the whole text above the English
        Tier3Only,    // translated only - only the tier 3 words glossed
    }

    // a three letter ISO 639-3 code. this used to be an enum, which cannot carry the
    // 8000 codes the standard defines, nor the font and direction each one needs to
    // be typeset - both of those come from LanguageNames and the configured language
    // table instead
    internal readonly record struct ISO639_3Code
    {
        internal static readonly ISO639_3Code eng = new("eng");

        internal String Code { get; }

        internal ISO639_3Code(String code)
        {
            Code = code;
        }

        public override String ToString() => Code;
    }

    internal enum SourceArchetype
    {
        Worksheet,
        QuestionSlides,
        Poster
    }


    // format
    // camelCase.tex
    // camelCase_workedSolutions.tex
    // camelCase_solutions.tex
    // camelCase_vocab.tex
    // camelCase/L2/pol/camelCase_polishKey.tex
    // camelCase/L2/pol/camelCase_polishParallelText.tex
    // camelCase/L2/pol/camelCase_workedSolutions_polishTier3Only.tex
    //
    // the language lives in a folder rather than in the name, so that a person
    // reading a served pdf filename sees "polish" rather than an ISO code few
    // people know, and so that a sheet's translations stay together on disk

    public partial class Orchestrator
    {

        private static String WorkedSolutionsIndicator = "workedSolutions";
        private static String SolutionsIndicator = "solutions";
        private static String VocabIndicator = "vocab";

        // the folder under a root that holds every translation of it
        private static String L2DirectoryName = "L2";

        // must match ContentRepository:SourceDirectory in appsettings.json
        private static String SourceDirectoryName = "latex";

        internal static String GetFilenameFromMetadata(SourceMetadata sm)
        {
            if (sm.Language != ISO639_3Code.eng)
            {
                return GetTranslatedFilename(sm);
            }

            StringBuilder sb = new StringBuilder(sm.RootName);

            AppendTypeSuffix(sb, sm.Type);

            if (sm.Rendition == SourceRendition.VocabKey)
            {
                sb.Append('_');
                sb.Append(VocabIndicator);
            }

            sb.Append(".tex");

            return sb.ToString();
        }

        // RootName/L2/<code>/<basename>[_type]_<languageName><Rendition>.tex
        private static String GetTranslatedFilename(SourceMetadata sm)
        {
            String? languageName = LanguageNames.EnglishNameOf(sm.Language.Code);

            if (languageName is null)
            {
                throw new ArgumentException(
                    $"'{sm.Language.Code}' is not a language we can name - add it to LanguageNames");
            }

            String basename = sm.RootName.Split('/').Last();

            StringBuilder sb = new StringBuilder(basename);

            AppendTypeSuffix(sb, sm.Type);

            sb.Append('_');
            sb.Append(languageName);
            sb.Append(RenditionSuffix(sm.Rendition));
            sb.Append(".tex");

            return String.Join('/', sm.RootName, L2DirectoryName, sm.Language.Code, sb.ToString());
        }

        private static void AppendTypeSuffix(StringBuilder sb, SourceType type)
        {
            switch (type)
            {
                case SourceType.Root:
                    break;
                case SourceType.WorkedSolutions:
                    {
                        sb.Append('_' + WorkedSolutionsIndicator);
                        break;
                    }
                case SourceType.Solutions:
                    {
                        sb.Append('_' + SolutionsIndicator);
                        break;
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        private static String RenditionSuffix(SourceRendition rendition)
        {
            switch (rendition)
            {
                case SourceRendition.L2Key:        return "Key";
                case SourceRendition.ParallelText: return "ParallelText";
                case SourceRendition.Tier3Only:    return "Tier3Only";
                default:
                    throw new ArgumentException(
                        $"{rendition} is not a rendition a translated file can have");
            }
        }

        // logger is optional so the parse stays static and testable - callers that have
        // one pass it so misnamed files get surfaced rather than silently absorbed
        internal static SourceMetadata ParseMetadataFromFilename(String filenameNoExt, ILogger? logger = null)
        {

            SourceArchetype at = ParseArchetype(filenameNoExt, logger);

            // english language defaults
            SourceMetadata smet = new SourceMetadata
            {
                Type      = SourceType.Root,
                Archetype = at,
                Language  = ISO639_3Code.eng,
                RootName  = filenameNoExt
            };

            if (filenameNoExt.Contains('/' + L2DirectoryName + '/', StringComparison.Ordinal))
            {
                return ParseTranslatedMetadata(filenameNoExt, smet, logger);
            }

            String[] parts = filenameNoExt.Split('_');

            // no suffix at all => an English root, so handle that here

            if (parts.Length == 1) /* short circuit */
            {
                return smet;
            }

            String rootName = String.Join('_', parts.Take(parts.Count() - 1));

            if (parts.Last() == WorkedSolutionsIndicator)
            {
                return smet with { Type = SourceType.WorkedSolutions, RootName = rootName };
            }

            if (parts.Last() == SolutionsIndicator)
            {
                return smet with { Type = SourceType.Solutions, RootName = rootName };
            }

            if (parts.Last() == VocabIndicator)
            {
                return smet with { Rendition = SourceRendition.VocabKey, RootName = rootName };
            }

            String isoCodeMaybe = parts.Last();

            if (isoCodeMaybe.Length != 3 || !LanguageNames.IsKnown(isoCodeMaybe))
            {
                return smet;
            }

            // a name ending in a language code we recognise. translations live in an L2
            // folder now, so this is either an old-style name or a coincidence - either
            // way the whole name is the root, but it is worth saying so
            logger?.LogWarning(
                "'{File}' ends in '_{Suffix}', which is a language code - translations live in "
                + "an {L2}/<code>/ folder now, so this is being treated as an English root. "
                + "Rename it if it was meant to be a translation.",
                filenameNoExt, isoCodeMaybe, L2DirectoryName);

            return smet;
        }

        // RootName/L2/<code>/<basename>[_type]_<languageName><Rendition>
        private static SourceMetadata ParseTranslatedMetadata(
            String filenameNoExt, SourceMetadata smet, ILogger? logger)
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

                return smet;
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

                return smet;
            }

            // the rendition is the tail after the language name, and the type is whatever
            // suffix sits in front of it
            int nameAt = filename.LastIndexOf('_' + languageName, StringComparison.Ordinal);

            if (nameAt < 0)
            {
                logger?.LogWarning(
                    "'{File}' is under {L2}/{Code}/ but its name does not carry '{Language}' - "
                    + "treating it as an English root.",
                    filenameNoExt, L2DirectoryName, code, languageName);

                return smet;
            }

            String beforeName = filename[..nameAt];
            String renditionPart = filename[(nameAt + 1 + languageName.Length)..];

            SourceRendition? rendition = ParseRendition(renditionPart);

            if (rendition is null)
            {
                logger?.LogWarning(
                    "'{File}' ends in '{Rendition}', which is not a translated form we produce - "
                    + "treating it as an English root.",
                    filenameNoExt, renditionPart);

                return smet;
            }

            SourceType type = SourceType.Root;

            if (beforeName.EndsWith('_' + WorkedSolutionsIndicator, StringComparison.Ordinal))
            {
                type = SourceType.WorkedSolutions;
            }
            else if (beforeName.EndsWith('_' + SolutionsIndicator, StringComparison.Ordinal))
            {
                type = SourceType.Solutions;
            }

            return smet with
            {
                RootName  = rootName,
                Language  = new ISO639_3Code(code),
                Type      = type,
                Rendition = (SourceRendition)rendition,
            };
        }

        private static SourceRendition? ParseRendition(String suffix)
        {
            switch (suffix)
            {
                case "Key":          return SourceRendition.L2Key;
                case "ParallelText": return SourceRendition.ParallelText;
                case "Tier3Only":    return SourceRendition.Tier3Only;
                default:             return null;
            }
        }

        // the archetype lives in the folder, not the filename - "latex/starters/..." is a deck
        // of question slides, "latex/cheatSheets/..." is a poster, and so on
        private static SourceArchetype ParseArchetype(String filenameNoExt, ILogger? logger)
        {
            SourceArchetype at = SourceArchetype.Worksheet; // default assumption for content in this repo

            // git reports paths with '/' on every platform, and RepoLogParser only keeps paths
            // under the source directory, so splitting on '/' is correct on Windows too
            String[] levels = filenameNoExt.Split('/');

            if (levels[0] != SourceDirectoryName)
            {
                // throwing here would take the whole pass down over a single path, and the
                // source directory is configurable, so say so loudly but carry on
                logger?.LogWarning(
                    "'{File}' is not under '{SourceDir}' - has the structure of the repository changed? "
                    + "treating it as a {Archetype}.",
                    filenameNoExt, SourceDirectoryName, at);

                return at;
            }

            if (levels.Length < 3)
            {
                // "latex/atype/nameofsource" is the bare minimum, so there is no folder to read
                logger?.LogWarning(
                    "'{File}' has no archetype folder, treating it as a {Archetype}.",
                    filenameNoExt, at);

                return at;
            }

            switch (levels[1])
            {
                case "worksheets":
                    at = SourceArchetype.Worksheet;
                    break;
                case "starters":
                    at = SourceArchetype.QuestionSlides;
                    break;
                case "cheatSheets":
                    at = SourceArchetype.Poster;
                    break;
                default:
                    logger?.LogWarning(
                        "unexpected folder seen: {Folder}, treating '{File}' as a {Archetype}.",
                        levels[1], filenameNoExt, at);
                    break;
            }

            return at;
        }
    }
}
