using System.Diagnostics;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        private List<TrackedFileWitMetadata> GetStaleFiles(HashSet<TrackedFileWitMetadata> fileFamily)
        {
            RootName rn = fileFamily.First().SourceMetadata.RootName;

            Debug.Assert(fileFamily.All(tfwm => tfwm.SourceMetadata.RootName == rn));

            // determine the staleness of the english files

            var englishFiles = fileFamily.Where(tfwm => tfwm.SourceMetadata.Language == ISO639_3Code.eng);

            CausalFileProcession procession = new CausalFileProcession(englishFiles);

            var englishStalenessInfo = procession.GetStalenessInfo();

            //List<TrackedFileWitMetadata> staleEnglishFiles = englishStalenessInfo.StaleFiles;

            if (fileFamily.Any(tfwm => tfwm.SourceMetadata.Language != ISO639_3Code.eng))
            {
                throw new NotImplementedException("need to implement handling of causaul translation snipping");
            }
            else
            {
                // only stale english
                // TODO - remove this logical branch once handle the foreign language file staleness
                return englishStalenessInfo.StaleFiles;
            }
        }
    }
}
