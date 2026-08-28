using SyntheticPDFs.Git;
using SyntheticPDFs.Models;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        // DI the services so can have pure functions below this

        private async Task<TexSourceModel> GenerateEnglishSyntheticSource(GenerationRequest request)
        {
            SourceMetadata sm = request.Target;

            if (request.Job == GenerationJob.CheckAnswerMacros)
            {
                return await CheckAnswerMacrosAndMarkWorkedSolutions(sm.RootName, RepoManager, LLMService);
            }

            switch (sm.Type)
            {
                case SourceType.Root:
                    throw new ArgumentException("can't generate English root source");
                case SourceType.WorkedSolutions:
                    {
                        return await GenerateSyntheticEnglishWorkedSolutions(sm.RootName, sm.Archetype, RepoManager, LLMService);
                    }
                case SourceType.Solutions:
                    {
                        return await GenerateSyntheticEnglishSolutions(sm.RootName, sm.Archetype, RepoManager, LLMService);
                    }
                default:
                    throw new NotImplementedException();
            }
        }


        private static async Task<TexSourceModel> GenerateSyntheticEnglishWorkedSolutions(RootName rootName, SourceArchetype at, IGitRepoManager gm, ILLMService LLM)
        {
            SourceMetadata rootMetadata = new SourceMetadata { Language = ISO639_3Code.eng, RootName = rootName, Type = SourceType.Root, Archetype = at };
            String rootFilename = GetFilenameFromMetadata(rootMetadata);
            TexSourceModel rootSource = gm.GetContent(rootFilename);

            if (at == SourceArchetype.QuestionSlides)
            {
                TexSourceModel? rewritten = await TryRewriteSlidesForAnswerMacros(rootSource, LLM);

                // the deck needed fixing, so that goes in first and the worked solutions
                // wait for the pass after - the rewritten root makes them stale anyway
                if (rewritten is not null) { return rewritten; }
            }

            String genSource = await SourceGenerator.GenerateSyntheticEnglishWorkedSolutionsTexSource(rootSource, at, LLM);

            if (at == SourceArchetype.QuestionSlides)
            {
                // the deck was just checked, so say so here rather than checking it again
                // next pass. a human edit makes these stale, which brings the check back
                genSource = AnswerMacros.AddVerifiedMarker(genSource);
            }

            SourceMetadata synthMetadata = rootMetadata with { Type = SourceType.WorkedSolutions };
            String synthFilename = GetFilenameFromMetadata(synthMetadata);

            return new TexSourceModel { FileNameFullPath = synthFilename, TexSource = genSource };
        }

        private static async Task<TexSourceModel> GenerateSyntheticEnglishSolutions(RootName rootName, SourceArchetype at, IGitRepoManager gm, ILLMService LLM)
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


        #region Answer overlay helpers

        // a deck that doesn't reveal its own answers has to be fixed before anything is
        // derived from it, so both jobs below can come back with the rewritten root instead
        // of the file they were asked for. the pass just commits whatever it is handed, and
        // the derived work comes round again once the deck has settled
        private static async Task<TexSourceModel?> TryRewriteSlidesForAnswerMacros(TexSourceModel rootSource, ILLMService LLM)
        {
            if (await SourceGenerator.QuestionSlidesUseAnswerMacros(rootSource, LLM))
            {
                return null;
            }

            String genSource = await SourceGenerator.GenerateSlidesWithAnswerMacrosTexSource(rootSource, LLM);

            return new TexSourceModel { FileNameFullPath = rootSource.FileNameFullPath, TexSource = genSource };
        }

        // worked solutions that are already in the repo but carry no record of a check - the
        // deck gets looked at, and if it is fine the existing file is stamped rather than
        // generated again, which would be paying twice for the same content
        private static async Task<TexSourceModel> CheckAnswerMacrosAndMarkWorkedSolutions(RootName rootName, IGitRepoManager gm, ILLMService LLM)
        {
            SourceMetadata rootMetadata = new SourceMetadata
            {
                Language  = ISO639_3Code.eng,
                RootName  = rootName,
                Type      = SourceType.Root,
                Archetype = SourceArchetype.QuestionSlides
            };

            TexSourceModel rootSource = gm.GetContent(GetFilenameFromMetadata(rootMetadata));

            TexSourceModel? rewritten = await TryRewriteSlidesForAnswerMacros(rootSource, LLM);

            if (rewritten is not null) { return rewritten; }

            SourceMetadata wsolMetadata = rootMetadata with { Type = SourceType.WorkedSolutions };
            String wsolFilename = GetFilenameFromMetadata(wsolMetadata);

            TexSourceModel wsolSource = gm.GetContent(wsolFilename);

            return new TexSourceModel
            {
                FileNameFullPath = wsolFilename,
                TexSource = AnswerMacros.AddVerifiedMarker(wsolSource.TexSource)
            };
        }

        #endregion
    }
}
