using System.Net.Http.Headers;

namespace SyntheticPDFs.Logic
{
    public static partial class SourceGenerator
    {
        private static String Requirements => "Just provide the contents of the worked solutions .tex file, nothing else, it MUST compile first time. It must define a document class and include imports for all packages that will be used";


        #region Archetype specific requirements
        private static String QuestionSlidesWorkedSolutionRequirements => "Put the worked solution for each distinc question in a slide on its own, there should be a contents slide that has links to the first worked solution for each of the question slides in the original file, so that the teacher can easily navigate to the start of the set of worked solutions that they need";
        #endregion


        private static String GenerateEnglishWorkedSolutionsPrompt(String rootSourceContents, SourceArchetype at)
        {
            switch (at)
            {
                case SourceArchetype.QuestionSlides:
                    return $"Below is the contents of a .tex file for slides of questions. Typeset worked solutions in LaTeX, showing clear workings with explanations. {Requirements} Original source: \n\n {rootSourceContents}";
                default:
                    return $"Below is the contents of a .tex file. Typeset worked solutions in LaTeX, showing clear workings with explanations. {QuestionSlidesWorkedSolutionRequirements}. {Requirements} Original source: \n\n {rootSourceContents}";
            }
        }

        // for now only use the worked solutions, if results are not good, then use the root source too!
        private static string GenerateEnglishSolutionsPrompt(String rootSourceContents, String wsolSourceContents)
        {
            return $"Below is the contents of a .tex file. It contains worked solutions, from these extract just the correct answers and produce a concise answer key for the questions. {Requirements} Original Source \n\n {wsolSourceContents}";
        }
    }
}
