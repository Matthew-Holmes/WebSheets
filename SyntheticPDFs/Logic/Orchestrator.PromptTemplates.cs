namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        private static String GenerateEnglishWorkedSolutionsPrompt(String rootSourceContents)
        {
            return $"Below is the contents of a .tex file. Typeset worked solutions in LaTeX, showing clear workings with explanations, just provide the contents of the worked solutions .tex file, nothing else, it MUST compile first time. Original source: \n\n {rootSourceContents}";
        }
    }
}
