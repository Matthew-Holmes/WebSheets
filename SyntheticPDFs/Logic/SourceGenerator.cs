using SyntheticPDFs.Models;
using SyntheticPDFs.Services;

namespace SyntheticPDFs.Logic
{
    public static partial class SourceGenerator
    {
        // As I see errors I can update these methods, or even find a library that will e.g. identify valid tex
        private static bool IsValidTex(string response)
        {
            bool OkFirstChar = response.First() == ' ' || response.First() == '\\' || response.First() == '%';

            // check begins match ends

            return OkFirstChar;
        }

        private static String TryFixupTex(String badTex)
        {
            // occasionally is wrapped in ```latex from the LLM, so strip the first and last and see if that helps!

            var lines = badTex.Split("\n");

            int nLines = lines.Count();

            // add trailing ends to the file

            return String.Join('\n', lines.Skip(1).Take(nLines - 2));
        }

        private static async Task<String?> TryGetValidTex(LLMService LLM, String prompt, int retry = 3)
        {
            for (int i = 0; i != retry; i++)
            {
                String response = await LLM.GetResponse(prompt);

                if (IsValidTex(response)) { return response; }

                LLM.Log(LogLevel.Warning, "Failed to generate good source");

                response = TryFixupTex(response);

                if (IsValidTex(response)) { return response; }

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
