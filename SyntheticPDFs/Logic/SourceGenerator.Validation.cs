namespace SyntheticPDFs.Logic
{
    public partial class SourceGenerator
    {
        private static bool OkFirstChar(String response)
        {
            return response.First() == ' ' || response.First() == '\\' || response.First() == '%';
        }

        private static bool LastLineIsTicks(String response)
        {
            return response.Split("\n").Last().Contains("```");
        }

        private static String RemoveFirstLine(String badTex)
        {
            var lines = badTex.Split("\n");

            return String.Join('\n', lines.Skip(1));
        }

        private static String RemoveLastLine(String badTex)
        {
            var lines = badTex.Split("\n");

            int nLines = lines.Count();

            return String.Join('\n', lines.Take(nLines - 1));
        }


        // As I see errors I can update these methods, or even find a library that will e.g. identify valid tex
        private static bool IsValidTex(String response)
        {
            bool okFirstLast = OkFirstChar(response) && !LastLineIsTicks(response);


            return okFirstLast;
        }

        private static String TryFixupTex(String badTex)
        {
            // occasionally is wrapped in ```latex from the LLM, so strip the first and last and see if that helps!

            if (!OkFirstChar(badTex))
            {
                badTex = RemoveFirstLine(badTex);
            }

            if (LastLineIsTicks(badTex))
            {
                badTex = RemoveLastLine(badTex);
            }

            return badTex;
        }

    }
}
