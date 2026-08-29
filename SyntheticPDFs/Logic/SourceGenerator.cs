using SyntheticPDFs.Models;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Logic
{
    public static partial class SourceGenerator
    {

        internal static async Task<String?> TryGetValidTex(ILLMService LLM, String prompt, int retry = 3)
        {
            for (int i = 0; i != retry; i++)
            {
                String response = await LLM.GetResponse(prompt);

                if (IsValidTex(response)) { return response; }

                LLM.Log(LogLevel.Warning, "Failed to generate good source");

                response = TryFixupTex(response, LLM);

                if (IsValidTex(response)) { return response; }

                LLM.Log(LogLevel.Warning, "Failed to fixup bad tex source");

                LLM.Log(LogLevel.Warning, $"attemtp {i + 1} at getting valid Tex failed!");
            }

            LLM.Log(LogLevel.Error, "failed to generate valide Tex!, returning null");

            return null;
        }

        // same shape as TryGetValidTex - the model gets a few goes at reaching a verdict
        // before we give up, since a shrug is not a verdict either way
        internal static async Task<ReviewVerdict?> TryGetReview(ILLMService LLM, String question, int retry = 3)
        {
            for (int i = 0; i != retry; i++)
            {
                ReviewVerdict? verdict = await LLM.GetReviewResponse(question);

                if (verdict is not null) { return verdict; }

                LLM.Log(LogLevel.Warning, $"attempt {i + 1} at getting a review verdict failed!");
            }

            LLM.Log(LogLevel.Error, "failed to get a review verdict!, returning null");

            return null;
        }

        internal static async Task<String> GenerateSyntheticEnglishWorkedSolutionsTexSource(TexSourceModel rootSource, SourceArchetype at, ILLMService LLM)
        {
            String prompt = GenerateEnglishWorkedSolutionsPrompt(rootSource.TexSource, at);

            String? texSource = await TryGetValidTex(LLM, prompt);

            if (texSource is null)
            {
                throw new Exception("failed to generate good source!");
            }

            return texSource;
        }



        internal static async Task<String> GenerateSyntheticEnglishSolutionsTexSource(TexSourceModel rootSource, TexSourceModel wsolSource, ILLMService LLM)
        {
            String prompt = GenerateEnglishSolutionsPrompt(rootSource.TexSource, wsolSource.TexSource);

            String? texSource = await TryGetValidTex(LLM, prompt);

            if (texSource is null)
            {
                throw new Exception("failed to generate good source!");
            }

            return texSource;
        }


        #region Answer overlay helpers

        // decks that reveal their own answers need no separate solutions pdf, so this is what
        // decides whether a deck is already doing that or needs the fixer run over it
        internal static async Task<ReviewVerdict> ReviewAnswerMacroUsage(TexSourceModel rootSource, ILLMService LLM)
        {
            // the definitions are free to check, and without them the verdict is settled
            if (!AnswerMacros.AreDefined(rootSource.TexSource))
            {
                return new ReviewVerdict
                {
                    Passed = false,
                    Reasons = "the deck does not define the answer overlay helpers verbatim in its preamble",
                };
            }

            ReviewVerdict? verdict = await TryGetReview(LLM, AnswerMacroUsageQuestion(rootSource.TexSource));

            if (verdict is null)
            {
                // treating a shrug as a pass ships an unchecked deck, as a fail it pays for a
                // rewrite that may not be needed, so do neither and let the next pass ask again
                throw new Exception("could not establish whether the answer overlay helpers are used!");
            }

            return verdict;
        }

        internal static async Task<String> GenerateSlidesWithAnswerMacrosTexSource(TexSourceModel rootSource, ILLMService LLM)
        {
            String prompt = GenerateAnswerMacroRewritePrompt(rootSource.TexSource);

            String? texSource = await TryGetValidTex(LLM, prompt);

            if (texSource is null)
            {
                throw new Exception("failed to generate good source!");
            }

            // refuse a rewrite that still fails the cheap check, otherwise it gets committed
            // and the next pass simply asks for the same rewrite again
            if (!AnswerMacros.AreDefined(texSource))
            {
                throw new Exception("rewritten slides still don't define the answer overlay helpers!");
            }

            return texSource;
        }

        internal static async Task<String> SummariseReviewReasons(IEnumerable<String> reasons, ILLMService LLM)
        {
            String summary = await LLM.GetSummaryResponse(SummariseReviewReasonsPrompt(reasons));

            if (String.IsNullOrWhiteSpace(summary))
            {
                throw new Exception("failed to summarise the review reasons!");
            }

            return summary;
        }

        #endregion
    }
}
