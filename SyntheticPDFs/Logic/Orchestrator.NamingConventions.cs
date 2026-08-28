using Microsoft.AspNetCore.Authorization.Infrastructure;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace SyntheticPDFs.Logic
{
    internal enum SourceType
    {
        Root,
        WorkedSolutions,
        Solutions,
    }

    internal enum ISO639_3Code
    {
        eng,
    }

    internal enum SourceArchetype
    {
        Worksheet,
        QuestionSlides,
        Poster
    }



    // format
    // camelCase.tex
    // camelCase_fra.tex
    // camelCase_workedSolutions.tex
    // camelCase_workedSolutions_fra.tex
    // camelCase_solutions.tex
    // camelCase_solutions_fra.tex

    public partial class Orchestrator
    {

        private static String WorkedSolutionsIndicator = "workedSolutions";
        private static String SolutionsIndicator = "solutions";

        // must match ContentRepository:SourceDirectory in appsettings.json
        private static String SourceDirectoryName = "latex";

        internal static String GetFilenameFromMetadata(SourceMetadata sm)
        {
            StringBuilder sb = new StringBuilder(sm.RootName);

            switch (sm.Type) 
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

            if (sm.Language != ISO639_3Code.eng)
            {
                sb.Append('_');
                sb.Append(Enum.GetName(typeof(ISO639_3Code), sm.Language));
            }

            sb.Append(".tex");

            return sb.ToString();

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


            String[] parts = filenameNoExt.Split('_');

            // no language code => english, so handle that here

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

            String isoCodeMaybe = parts.Last();

            if (isoCodeMaybe.Length != 3)
            {
                return smet;
            }

            // foreign language variants

            ISO639_3Code? isoCode = ParseIso639_3(isoCodeMaybe);

            if (isoCode is null)
            {
                // not a language we know, so it is just part of the name - a sheet called
                // foo_abc.tex is a root, not a translation. treating this as an error would
                // take the whole service down over an ordinary filename, but it is worth
                // saying so: it is equally likely to be a translation we cannot handle yet
                logger?.LogWarning(
                    "'{File}' ends in '_{Suffix}', which is not a language code we recognise - "
                    + "treating the whole name as an English root. Add {Suffix} to ISO639_3Code "
                    + "if this was meant to be a translation.",
                    filenameNoExt, isoCodeMaybe, isoCodeMaybe);

                return smet;
            }

            rootName = String.Join('_', parts.Take(parts.Count() - 2));

            SourceMetadata smetL2 = smet with { Language = (ISO639_3Code)isoCode};


            if (parts[parts.Count()-2] == WorkedSolutionsIndicator)
            {
                return smetL2 with { Type = SourceType.WorkedSolutions, RootName = rootName };
            }

            if (parts[parts.Count()-2] == SolutionsIndicator)
            {
                return smetL2 with { Type = SourceType.Solutions, RootName = rootName };
            }

            rootName = String.Join('_', parts.Take(parts.Count() - 1));


            return smetL2 with { RootName = rootName };

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


        private static ISO639_3Code? ParseIso639_3(String code)
            {

                if (Enum.TryParse<ISO639_3Code>(code, ignoreCase: true, out var result) &&
                    Enum.IsDefined(typeof(ISO639_3Code), result))
                {
                    return result;
                }

                return null;
            }

        }
}
