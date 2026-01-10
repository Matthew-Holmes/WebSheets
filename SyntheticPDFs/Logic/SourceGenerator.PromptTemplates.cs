namespace SyntheticPDFs.Logic
{
    public static partial class SourceGenerator
    {
        private static String Requirements => "Just provide the contents of the worked solutions .tex file, nothing else, it MUST compile first time.";


        private static String GenerateEnglishWorkedSolutionsPrompt(String rootSourceContents)
        {
            return $"Below is the contents of a .tex file. Typeset worked solutions in LaTeX, showing clear workings with explanations. {Requirements} Original source: \n\n {rootSourceContents}";
        }

        // for now only use the worked solutions, if results are not good, then use the root source too!
        private static string GenerateEnglishSolutionsPrompt(String rootSourceContents, String wsolSourceContents)
        {
            return $"Below is the contents of a .tex file. It contains worked solutions, from these extract just the correct answers and produce a concise answer key for the questions. {Requirements} Original Source \n\n {wsolSourceContents}";
        }
    }
}
