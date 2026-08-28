namespace SyntheticPDFs.Logic
{
    public static partial class SourceGenerator
    {
        private static String Requirements => "Just provide the contents of the worked solutions .tex file, nothing else, it MUST compile first time. It must define a document class and include imports for all packages that will be used";

        private static String RewriteRequirements => "Just provide the contents of the rewritten .tex file, nothing else, it MUST compile first time. Keep the document class and add imports for any packages the helpers need";


        #region Archetype specific requirements
        private static String QuestionSlidesWorkedSolutionRequirements => "Put the worked solution for each distinct question in a slide on its own, there should be a contents slide that has links to the first worked solution for each of the question slides in the original file, so that the teacher can easily navigate to the start of the set of worked solutions that they need. That should link to a copy of the questions and short answers (using the ashow helpers) as per the original, after those two slides, proceed with adding the worked solution slides, one slide per question's worked solutions";
        #endregion


        private static String GenerateEnglishWorkedSolutionsPrompt(String rootSourceContents, SourceArchetype at)
        {
            switch (at)
            {
                case SourceArchetype.QuestionSlides:
                    return $"Below is the contents of a .tex file for slides of questions. Typeset worked solutions in LaTeX, showing clear workings with explanations. {QuestionSlidesWorkedSolutionRequirements}. {AnswerMacroUsageRules} {Requirements} Original source: \n\n {rootSourceContents}";
                default:
                    return $"Below is the contents of a .tex file. Typeset worked solutions in LaTeX, showing clear workings with explanations. {Requirements} Original source: \n\n {rootSourceContents}";
            }
        }

        // for now only use the worked solutions, if results are not good, then use the root source too!
        private static string GenerateEnglishSolutionsPrompt(String rootSourceContents, String wsolSourceContents)
        {
            return $"Below is the contents of a .tex file. It contains worked solutions, from these extract just the correct answers and produce a concise answer key for the questions. {Requirements} Original Source \n\n {wsolSourceContents}";
        }


        #region Answer overlay helpers

        // how the helpers have to be used, said the same way in every prompt that asks for
        // them. \ablank typesets its argument twice - once for real and once inside a
        // \phantom to reserve the width - so an argument that only works in text mode, or
        // that isn't one well formed group, takes the whole build down rather than looking
        // slightly wrong. each of these is a compile error we would otherwise only find out
        // about from a failed build
        private static String AnswerMacroUsageRules => String.Join(' ',
            @"The answer always goes inside the braces of \ablank, \ashow or \ashowq, never beside the macro.",
            @"An answer that is mathematical must be in inline math mode inside those braces: write \ablank{$3x^2$}, never \ablank{3x^2}.",
            "Anything with a superscript, subscript, fraction, root, integral or Greek letter counts as mathematical.",
            @"Inside the braces use inline math only, never \[ \], never $$ $$ and never a display environment, since the argument is measured and underlined.",
            "If the macro is already inside math mode, do not open math mode again inside the braces.",
            @"The argument must be a single balanced group: no blank lines, no \\, no & and nothing verbatim.",
            @"Outside math mode, escape %, &, # and _ in the answer.",
            @"Do not add size or colour commands, \ashow and \ashowq already apply \small and red.",
            @"Keep \ablank answers short, since it reserves the width of the answer in the question line.",
            @"Write mathematical symbols as LaTeX commands such as \implies and \approx, never as unicode characters.",
            "Only use the helpers in the body of a frame, never in a frame title or a section heading.");

        // the deck is known to define the helpers by the time this gets asked - what is left
        // is whether they are actually used, and used in a way that will compile
        private static String AnswerMacroUsageQuestion(String rootSourceContents)
        {
            return "Below is the contents of a .tex file for slides of questions. It defines "
                + @"\ablank, \ashow and \ashowq, which reveal answers on a second overlay. "
                + "Is every question in this deck given its answer through one of those three macros? "
                + "Answer NO if any question is left without an answer, or if any answer is typeset "
                + "without using them. "
                + "Answer NO if any mathematical answer inside those macros is not in inline math mode, "
                + @"so \ablank{3x^2} is a NO where \ablank{$3x^2$} is fine. "
                + $"Source: \n\n {rootSourceContents}";
        }

        private static String GenerateAnswerMacroRewritePrompt(String rootSourceContents)
        {
            return "Below is the contents of a .tex file for slides of questions. Rewrite it so that "
                + "every question reveals its answer on a second overlay of the same slide, using "
                + @"\ablank for an answer that fills a gap in a sentence, \ashow for an answer shown "
                + @"alongside its question, and \ashowq for an answer that replaces a question mark. "
                + "These definitions must appear verbatim in the preamble: "
                + $"\n\n{AnswerMacros.Definitions}\n\n"
                + "Leave the questions themselves unchanged - only the answers are being added. "
                + "If the deck already puts its solutions on separate slides then keep those slides "
                + "exactly as they are, the helpers are needed as well, not instead. "
                + $"{AnswerMacroUsageRules} "
                + $"{RewriteRequirements} Original source: \n\n {rootSourceContents}";
        }

        #endregion
    }
}
