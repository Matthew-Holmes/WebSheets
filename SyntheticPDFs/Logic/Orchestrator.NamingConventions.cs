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
            String[] parts = filenameNoExt.Split('_');

            // english language defaults

            if (parts.Length == 1) /* short circuit */
            {
                return new SourceMetadata { Type = SourceType.Root, Language = ISO639_3Code.eng, RootName = filenameNoExt };
            }

            if (parts.Last() == WorkedSolutionsIndicator)
            {
                String rootName = String.Join('_', parts.Take(parts.Count() - 1));
                return new SourceMetadata { Type = SourceType.WorkedSolutions, Language = ISO639_3Code.eng, RootName = rootName };
            }

            if (parts.Last() == SolutionsIndicator)
            {
                String rootName = String.Join('_', parts.Take(parts.Count() - 1));
                return new SourceMetadata { Type = SourceType.Solutions, Language = ISO639_3Code.eng, RootName = rootName };
            }

            String isoCodeMaybe = parts.Last();

            if (isoCodeMaybe.Length != 3)
            {
                return new SourceMetadata { Type = SourceType.Root, Language = ISO639_3Code.eng, RootName = filenameNoExt };
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

                return new SourceMetadata { Type = SourceType.Root, Language = ISO639_3Code.eng, RootName = filenameNoExt };
            }


            if (parts[parts.Count()-2] == WorkedSolutionsIndicator)
            {
                String rootName = String.Join('_', parts.Take(parts.Count() - 2));
                return new SourceMetadata { Type = SourceType.WorkedSolutions, Language = (ISO639_3Code)isoCode, RootName = rootName };
            }

            if (parts[parts.Count()-2] == SolutionsIndicator)
            {
                String rootName = String.Join('_', parts.Take(parts.Count() - 2));
                return new SourceMetadata { Type = SourceType.Solutions, Language = (ISO639_3Code)isoCode, RootName = rootName };
            }

            String rootName_ = String.Join('_', parts.Take(parts.Count() - 1));

            return new SourceMetadata { Type = SourceType.Root, Language = (ISO639_3Code)isoCode, RootName = rootName_ };

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
