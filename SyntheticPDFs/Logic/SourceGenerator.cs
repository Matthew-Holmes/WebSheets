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

        // same shape as TryGetValidTex - the model gets a few goes at committing to a verdict
        // before we give up, since a shrug is not an answer either way
        internal static async Task<bool?> TryGetYesNoAnswer(ILLMService LLM, String question, int retry = 3)
        {
            for (int i = 0; i != retry; i++)
            {
                bool? answer = await LLM.GetYesNoResponse(question);

                if (answer is not null) { return answer; }

                LLM.Log(LogLevel.Warning, $"attempt {i + 1} at getting a yes/no answer failed!");
            }

            LLM.Log(LogLevel.Error, "failed to get a yes/no answer!, returning null");

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

        // decks that reveal their own answers need no separate solutions pdf, so this is
        // what decides whether a deck is already doing that or needs rewriting
        internal static async Task<bool> QuestionSlidesUseAnswerMacros(TexSourceModel rootSource, ILLMService LLM)
        {
            // the definitions are free to check, and without them the answer is settled
            if (!AnswerMacros.AreDefined(rootSource.TexSource)) { return false; }

            bool? used = await TryGetYesNoAnswer(LLM, AnswerMacroUsageQuestion(rootSource.TexSource));

            if (used is null)
            {
                // guessing yes ships an unchecked deck, guessing no pays for a rewrite that
                // may not be needed, so do neither and let the next pass ask again
                throw new Exception("could not establish whether the answer overlay helpers are used!");
            }

            return (bool)used;
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

        #endregion
    }
}
