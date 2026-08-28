using SyntheticPDFs.Git;
using SyntheticPDFs.Models;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        internal async Task<TexSourceModel> GenerateSyntheticSource(GenerationRequest request)
        {
            if (request.Target.Language == ISO639_3Code.eng)
            {
                return await GenerateEnglishSyntheticSource(request);
            } else
            {
                return await GenerateForeignLanguageSyntheticSource(request);
            }
        }
    }
}
