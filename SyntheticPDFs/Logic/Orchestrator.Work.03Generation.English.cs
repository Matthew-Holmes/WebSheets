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
                        return await GenerateSytheticEnglishWorkedSolutions(sm.RootName, sm.Archetype, RepoManager, LLMService);
                    }
                case SourceType.Solutions:
                    {
                        return await GenerateSytheticEnglishSolutions(sm.RootName, sm.Archetype, RepoManager, LLMService);
                    }
                default:
                    throw new NotImplementedException();
            }
        }


        private static async Task<TexSourceModel> GenerateSytheticEnglishWorkedSolutions(RootName rootName, SourceArchetype at, IGitRepoManager gm, ILLMService LLM)
        {
            SourceMetadata rootMetadata = new SourceMetadata { Language = ISO639_3Code.eng, RootName = rootName, Type = SourceType.Root, Archetype = at };
            String rootFilename = GetFilenameFromMetadata(rootMetadata);
            TexSourceModel rootSource = gm.GetContent(rootFilename);

            String genSource = await SourceGenerator.GenerateSyntheticEnglishWorkedSolutionsTexSource(rootSource, at, LLM);

            SourceMetadata synthMetadata = rootMetadata with { Type = SourceType.WorkedSolutions };
            String synthFilename = GetFilenameFromMetadata(synthMetadata);

            return new TexSourceModel { FileNameFullPath = synthFilename, TexSource = genSource };
        }

        private static async Task<TexSourceModel> GenerateSytheticEnglishSolutions(RootName rootName, SourceArchetype at, IGitRepoManager gm, ILLMService LLM)
        {
            SourceMetadata rootMetadata = new SourceMetadata { Language = ISO639_3Code.eng, RootName = rootName, Type = SourceType.Root,            Archetype = at, };
            SourceMetadata wsolMetadata = new SourceMetadata { Language = ISO639_3Code.eng, RootName = rootName, Type = SourceType.WorkedSolutions, Archetype = at };

            String rootFilename = GetFilenameFromMetadata(rootMetadata);
            String wsolFilename = GetFilenameFromMetadata(wsolMetadata);

            TexSourceModel rootSource = gm.GetContent(rootFilename);
            TexSourceModel wsolSource = gm.GetContent(wsolFilename);

            // TODO extract logic about which archetype is allowed which source type and then reuse that here to check that not generating stuff we shouldn't
            // is there a smarter way to refactor all this??

            String genSource = await SourceGenerator.GenerateSyntheticEnglishSolutionsTexSource(rootSource, wsolSource, LLM);

            SourceMetadata synthMetadata = rootMetadata with { Type = SourceType.Solutions };
            String synthFilename = GetFilenameFromMetadata(synthMetadata);

            return new TexSourceModel { FileNameFullPath = synthFilename, TexSource = genSource };
        }
    }
}
