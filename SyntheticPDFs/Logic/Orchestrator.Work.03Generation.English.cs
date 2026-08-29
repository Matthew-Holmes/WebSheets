using SyntheticPDFs.Git;
using SyntheticPDFs.Models;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    public partial class Orchestrator
    {
        // DI the services so can have pure functions below this

        private async Task<List<TexSourceModel>> GenerateEnglishSyntheticSource(GenerationRequest request)
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


        private static async Task<List<TexSourceModel>> GenerateSyntheticEnglishWorkedSolutions(RootName rootName, SourceArchetype at, IGitRepoManager gm, ILLMService LLM)
        {
            SourceMetadata rootMetadata = new SourceMetadata { Language = ISO639_3Code.eng, RootName = rootName, Type = SourceType.Root, Archetype = at };
            String rootFilename = GetFilenameFromMetadata(rootMetadata);
            TexSourceModel rootSource = gm.GetContent(rootFilename);

            List<TexSourceModel> ret = new();

            if (at == SourceArchetype.QuestionSlides)
            {
                // the review settles within this job now, so the deck and its worked solutions
                // go in together - a batch lands in one commit, and IsYounger allows equal ages
                AnswerMacroReviewOutcome review = await ReviewAndFixAnswerMacros(rootSource, LLM);

                if (review.DeckChanged) { ret.Add(review.Deck); }

                rootSource = review.Questions;
            }

            String genSource = await SourceGenerator.GenerateSyntheticEnglishWorkedSolutionsTexSource(rootSource, at, LLM);

            if (at == SourceArchetype.QuestionSlides)
            {
                // the deck has been settled one way or another, so record that here and stop
                // reviewing it. a human edit makes these stale, which brings the review back
                genSource = AnswerMacros.AddVerifiedMarker(genSource);
            }

            SourceMetadata synthMetadata = rootMetadata with { Type = SourceType.WorkedSolutions };
            String synthFilename = GetFilenameFromMetadata(synthMetadata);

            ret.Add(new TexSourceModel { FileNameFullPath = synthFilename, TexSource = genSource });

            return ret;
        }

        private static async Task<List<TexSourceModel>> GenerateSyntheticEnglishSolutions(RootName rootName, SourceArchetype at, IGitRepoManager gm, ILLMService LLM)
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

            return new List<TexSourceModel> { new TexSourceModel { FileNameFullPath = synthFilename, TexSource = genSource } };
        }


        #region Answer overlay helpers

        // worked solutions that are already in the repo but carry no record of a review - the
        // deck gets looked at, and if it settles the existing file is stamped rather than
        // generated again, which would be paying twice for the same content
        private static async Task<List<TexSourceModel>> CheckAnswerMacrosAndMarkWorkedSolutions(RootName rootName, IGitRepoManager gm, ILLMService LLM)
        {
            SourceMetadata rootMetadata = new SourceMetadata
            {
                Language  = ISO639_3Code.eng,
                RootName  = rootName,
                Type      = SourceType.Root,
                Archetype = SourceArchetype.QuestionSlides
            };

            TexSourceModel rootSource = gm.GetContent(GetFilenameFromMetadata(rootMetadata));

            AnswerMacroReviewOutcome review = await ReviewAndFixAnswerMacros(rootSource, LLM);

            List<TexSourceModel> ret = new();

            if (review.DeckChanged) { ret.Add(review.Deck); }

            SourceMetadata wsolMetadata = rootMetadata with { Type = SourceType.WorkedSolutions };
            String wsolFilename = GetFilenameFromMetadata(wsolMetadata);

            TexSourceModel wsolSource = gm.GetContent(wsolFilename);

            ret.Add(new TexSourceModel
            {
                FileNameFullPath = wsolFilename,
                TexSource = AnswerMacros.AddVerifiedMarker(wsolSource.TexSource)
            });

            return ret;
        }

        #endregion
    }
}
