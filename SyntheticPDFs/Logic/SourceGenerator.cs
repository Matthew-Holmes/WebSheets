using SyntheticPDFs.Models;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Logic
{
    public static partial class SourceGenerator
    {
        // As I see errors I can update these methods, or even find a library that will e.g. identify valid tex
        private static bool IsValidTex(string response)
        {
            return true;
        }

        private static String TryFixupTex(String badTex)
        {
            return badTex;
        }

        private static async Task<String?> TryGetValidTex(LLMService LLM, String prompt, int retry = 3)
        {
            for (int i = 0; i != retry; i++)
            {
                String response = await LLM.GetResponse(prompt);

                if (IsValidTex(prompt)) { return response; }

                LLM.Log(LogLevel.Warning, "Failed to generate good source");

                response = TryFixupTex(response);

                if (IsValidTex(prompt)) { return response; }

                LLM.Log(LogLevel.Warning, "Failed to fixup bad tex source");

                LLM.Log(LogLevel.Warning, $"attemtp {i + 1} at getting valid Tex failed!");
            }

            LLM.Log(LogLevel.Error, "failed to generate valide Tex!, returning null");

            return null;
        }

        internal static async Task<String> GenerateSyntheticEnglishWorkedSolutionsTexSource(TexSourceModel rootSource, LLMService LLM)
        {
            String prompt = GenerateEnglishWorkedSolutionsPrompt(rootSource.TexSource);

            String? texSource = await TryGetValidTex(LLM, prompt);

            if (texSource is null)
            {
                throw new Exception("failed to generate good source!");
            }

            return texSource;
        }



        internal static async Task<String> GenerateSyntheticEnglishSolutionsTexSource(TexSourceModel rootSource, TexSourceModel wsolSource, LLMService LLM)
        {
            String prompt = GenerateEnglishSolutionsPrompt(rootSource.TexSource, wsolSource.TexSource);

            String? texSource = await TryGetValidTex(LLM, prompt);

            if (texSource is null)
            {
                throw new Exception("failed to generate good source!");
            }

            return texSource;
        }


    }
}
