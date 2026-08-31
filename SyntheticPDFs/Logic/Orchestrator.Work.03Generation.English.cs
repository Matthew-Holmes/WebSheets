using SyntheticPDFs.Git;
using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        // The services are passed in rather than reached for, so that everything below
        // this line is a function of its arguments.
        private async Task<List<TexSourceModel>> GenerateEnglishSyntheticSource(
            GenerationRequest request)
        {
            SourceMetadata sm = request.Target;

            if (request.Job == GenerationJob.CheckAnswerMacros)
            {
                return await CheckAnswerMacrosAndMarkWorkedSolutions(
                    sm with { Part = SheetPart.Root }, RepoManager, LLMService);
            }

            switch (sm.Part)
            {
                case SheetPart.Root:
                    throw new ArgumentException("can't generate English root source");

                case SheetPart.WorkedSolutions:
                    return await GenerateSyntheticEnglishWorkedSolutions(sm, RepoManager, LLMService);

                case SheetPart.Solutions:
                    return await GenerateSyntheticEnglishSolutions(sm, RepoManager, LLMService);

                default:
                    throw new NotImplementedException();
            }
        }

        private static async Task<List<TexSourceModel>> GenerateSyntheticEnglishWorkedSolutions(
            SourceMetadata target, IGitRepoManager gm, ILLMService LLM)
        {
            SheetArchetype at = target.Archetype;

            SourceMetadata rootMetadata = target with { Part = SheetPart.Root };

            TexSourceModel rootSource = gm.GetContent(rootMetadata.FilePath);

            List<TexSourceModel> ret = new();

            if (at.RevealsItsOwnAnswers)
            {
                // the review settles within this job now, so the deck and its worked
                // solutions go in together - a batch lands in one commit, and a file is
                // allowed to be exactly as old as what it came from
                AnswerMacroReviewOutcome review = await ReviewAndFixAnswerMacros(rootSource, LLM);

                if (review.DeckChanged) { ret.Add(review.Deck); }

                rootSource = review.Questions;
            }

            String genSource =
                await SourceGenerator.GenerateSyntheticEnglishWorkedSolutionsTexSource(
                    rootSource, at, LLM);

            if (at.RevealsItsOwnAnswers)
            {
                // the deck has been settled one way or another, so record that here and
                // stop reviewing it. a human edit makes these stale, which brings the
                // review back
                genSource = AnswerMacros.AddVerifiedMarker(genSource);
            }

            ret.Add(new TexSourceModel
            {
                FileNameFullPath = (rootMetadata with { Part = SheetPart.WorkedSolutions }).FilePath,
                TexSource        = genSource,
            });

            return ret;
        }

        private static async Task<List<TexSourceModel>> GenerateSyntheticEnglishSolutions(
            SourceMetadata target, IGitRepoManager gm, ILLMService LLM)
        {
            SourceMetadata rootMetadata = target with { Part = SheetPart.Root };

            TexSourceModel rootSource = gm.GetContent(rootMetadata.FilePath);

            TexSourceModel wsolSource = gm.GetContent(
                (rootMetadata with { Part = SheetPart.WorkedSolutions }).FilePath);

            String genSource = await SourceGenerator.GenerateSyntheticEnglishSolutionsTexSource(
                rootSource, wsolSource, LLM);

            return new List<TexSourceModel>
            {
                new TexSourceModel
                {
                    FileNameFullPath = (rootMetadata with { Part = SheetPart.Solutions }).FilePath,
                    TexSource        = genSource,
                },
            };
        }

        #region Answer overlay helpers

        // Worked solutions that are already in the repository but carry no record of a
        // review - the deck gets looked at, and if it settles the existing file is stamped
        // rather than generated again, which would be paying twice for the same content.
        private static async Task<List<TexSourceModel>> CheckAnswerMacrosAndMarkWorkedSolutions(
            SourceMetadata rootMetadata, IGitRepoManager gm, ILLMService LLM)
        {
            TexSourceModel rootSource = gm.GetContent(rootMetadata.FilePath);

            AnswerMacroReviewOutcome review = await ReviewAndFixAnswerMacros(rootSource, LLM);

            List<TexSourceModel> ret = new();

            if (review.DeckChanged) { ret.Add(review.Deck); }

            String wsolFilename = (rootMetadata with { Part = SheetPart.WorkedSolutions }).FilePath;

            TexSourceModel wsolSource = gm.GetContent(wsolFilename);

            ret.Add(new TexSourceModel
            {
                FileNameFullPath = wsolFilename,
                TexSource        = AnswerMacros.AddVerifiedMarker(wsolSource.TexSource),
            });

            return ret;
        }

        #endregion
    }
}
