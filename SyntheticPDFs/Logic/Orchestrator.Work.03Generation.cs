using SyntheticPDFs.Git;
using SyntheticPDFs.Models;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        // one request can settle more than one file - a slide deck that needed fixing comes
        // back alongside the worked solutions derived from it
        internal async Task<List<TexSourceModel>> GenerateSyntheticSource(GenerationRequest request)
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
