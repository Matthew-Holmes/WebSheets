using Microsoft.AspNetCore.Mvc.Razor;
using SyntheticPDFs.Models;
using static System.Net.Mime.MediaTypeNames;

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

        internal static SourceMetadata ParseMetadataFromFilename(String filenameNoExt)
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
                // really we need to ban root filenames ending in _ISO - but realistically that will happen
                // if I stick to camelCase
                throw new NotImplementedException($"need to implement the isocode {isoCodeMaybe}");
            }


            if (parts[parts.Count()-2] == WorkedSolutionsIndicator)
            {
                String rootName = String.Join('_', parts.Take(parts.Count() - 2));
                return new SourceMetadata { Type = SourceType.WorkedSolutions, Language = (ISO639_3Code)isoCode, RootName = rootName };
            }

            if (parts[parts.Count()-2] == SolutionsIndicator)
            {
                String rootName = String.Join('_', parts.Take(parts.Count() - 2));
                return new SourceMetadata { Type = SourceType.WorkedSolutions, Language = (ISO639_3Code)isoCode, RootName = rootName };
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
