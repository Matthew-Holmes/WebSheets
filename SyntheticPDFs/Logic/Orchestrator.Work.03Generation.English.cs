using SyntheticPDFs.Git;
using SyntheticPDFs.Models;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        // DI the services so can have pure functions below this

        private async Task<TexSourceModel> GenerateEnglishSyntheticSource(SourceMetadata sm)
        {
            switch (sm.Type)
            {
                case SourceType.Root:
                    throw new ArgumentException("can't generate English root source");
                case SourceType.WorkedSolutions:
                    {
                        return await GenerateSytheticEnglishWorkedSolutions(sm.RootName, RepoManager, LLMService);
                    }
                case SourceType.Solutions:
                    {
                        return await GenerateSytheticEnglishSolutions(sm.RootName, RepoManager, LLMService);
                    }
                default:
                    throw new NotImplementedException();
            }
        }


        private static async Task<TexSourceModel> GenerateSytheticEnglishWorkedSolutions(RootName rootName, GitRepoManager gm, LLMService LLM)
        {
            SourceMetadata rootMetadata = new SourceMetadata { Language = ISO639_3Code.eng, RootName = rootName, Type = SourceType.Root };
            String rootFilename = GetFilenameFromMetadata(rootMetadata);
            TexSourceModel rootSource = gm.GetContent(rootFilename);

            String genSource = await SourceGenerator.GenerateSyntheticEnglishWorkedSolutionsTexSource(rootSource, LLM);

            SourceMetadata synthMetadata = rootMetadata with { Type = SourceType.WorkedSolutions };
            String synthFilename = GetFilenameFromMetadata(synthMetadata);

            return new TexSourceModel { FileNameFullPath = synthFilename, TexSource = genSource };
        }

        private static async Task<TexSourceModel> GenerateSytheticEnglishSolutions(RootName rootName, GitRepoManager gm, LLMService LLM)
        {
            SourceMetadata rootMetadata = new SourceMetadata { Language = ISO639_3Code.eng, RootName = rootName, Type = SourceType.Root };
            SourceMetadata wsolMetadata = new SourceMetadata { Language = ISO639_3Code.eng, RootName = rootName, Type = SourceType.WorkedSolutions };

            String rootFilename = GetFilenameFromMetadata(rootMetadata);
            String wsolFilename = GetFilenameFromMetadata(wsolMetadata);

            TexSourceModel rootSource = gm.GetContent(rootFilename);
            TexSourceModel wsolSource = gm.GetContent(wsolFilename);

            String genSource = await SourceGenerator.GenerateSyntheticEnglishSolutionsTexSource(rootSource, wsolSource, LLM);

            SourceMetadata synthMetadata = rootMetadata with { Type = SourceType.Solutions };
            String synthFilename = GetFilenameFromMetadata(synthMetadata);

            return new TexSourceModel { FileNameFullPath = synthFilename, TexSource = genSource };
        }
    }
}
