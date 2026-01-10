using SyntheticPDFs.Git;
using SyntheticPDFs.Models;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        private async Task<TexSourceModel> GenerateSyntheticSource(SourceMetadata sm)
        {
            if (sm.Language == ISO639_3Code.eng)
            {
                return await GenerateEnglishSyntheticSource(sm);
            } else
            {
                return await GenerateForeignLanguageSyntheticSource(sm);
            }
        }
    }
}
