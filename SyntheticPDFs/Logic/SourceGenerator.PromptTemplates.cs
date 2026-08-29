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
        // there is more than one way to reveal an answer and none of them is the right one.
        // what every prompt is told is the goal - the answer appears on overlay 2 - and then
        // which helper suits which kind of answer, plus the things that stop a deck compiling
        private static String AnswerMacroUsageRules => String.Join(' ',
            "Every answer must appear on overlay 2 of its own slide, so that it reveals on the next slide",
            "of the compiled pdf. Which helper does that is a matter of what reads best on the slide.",
            @"Use \ablank for an answer that fills a gap in a sentence or an equation, \ashow for an answer",
            @"shown alongside its question, and \ashowq for an answer that replaces a question mark, including",
            "a short label on a diagram.",
            "For an answer that is better drawn than written, add it to the picture the slide already has and",
            "give it the ans style for a line or a shape, ansfill for a shaded region, and anslab for a text",
            "label. Those are TikZ styles, so they go in the options of a node or a path, as in",
            @"\draw[ans] or \node[anslab].",
            "Pick whichever of these makes the answer clearest, and use different ones on the same slide if",
            "that reads better.",
            @"The answer always goes inside the braces of \ablank, \ashow or \ashowq, never beside the macro.",
            @"An answer that is mathematical must be in inline math mode inside those braces: write \ablank{$3x^2$}, never \ablank{3x^2}.",
            "Anything with a superscript, subscript, fraction, root, integral or Greek letter counts as mathematical.",
            @"Inside the braces use inline math only, never \[ \], never $$ $$ and never a display environment, since the argument is measured to reserve its space.",
            "If the macro is already inside math mode, do not open math mode again inside the braces.",
            @"The argument must be a single balanced group: no blank lines, no \\, no & and nothing verbatim.",
            @"Outside math mode, escape %, &, # and _ in the answer.",
            @"Do not add size or colour commands, the helpers already apply \small and red.",
            @"Keep \ablank answers short, since it reserves the width of the answer in the question line.",
            @"Write mathematical symbols as LaTeX commands such as \implies and \approx, never as unicode characters.",
            "Only use the helpers in the body of a frame, never in a frame title or a section heading.");

        // the things a reviewer must NOT fail a deck for. every one of these has been seen, or
        // is a plain misreading of the source, and each false rejection used to cost a full
        // rewrite of the deck. the reviewer is deliberately given a narrow definition of
        // broken - a question with no answer, or an answer that will not compile
        private static String AnswerMacroReviewExclusions => String.Join(' ',
            @"Read only the LaTeX that will actually be typeset. Anything commented out with % does not exist:",
            "a commented out answers slide, or a commented out frame, is not a problem and is not an answer",
            "typeset outside the helpers.",
            "Only questions count. Titles, section headings, contents slides, instructions, prompts to discuss",
            "something, and worked examples are not questions and need no answer.",
            "Any of the ways of revealing an answer is as good as any other, and the deck is free to mix them.",
            "Never fail a deck because one helper was used where you would have chosen another.",
            "An answer drawn on to a diagram counts as answered, whether it uses the ans, ansfill or anslab",
            @"styles or a short \ashowq label - the answer does not have to be words or numbers.",
            "One macro may answer several parts at once, and a question may have its answer shown anywhere on",
            "its slide.",
            @"A helper used inside existing math mode, such as $x = \ablank{52}$, is correct, and the braces do",
            "not need dollar signs of their own there.",
            "Numbering questions by hand, rather than with an enumerate, is fine.",
            "Do not fail a deck over style, wording, spacing, layout, slide order, consistency between slides,",
            "or anything you would merely prefer done differently.");

        // the deck is known to define the helpers by the time this gets asked - what is left
        // is whether every answer reveals on overlay 2, in a way that will compile
        private static String AnswerMacroUsageQuestion(String rootSourceContents)
        {
            return "Below is the contents of a .tex file for slides of questions. It defines "
                + @"\ablank, \ashow and \ashowq, and the TikZ styles ans, ansfill and anslab for answers "
                + "drawn on to a diagram. All of them keep the answer hidden on overlay 1 and reveal it on "
                + "overlay 2, so the answers appear on the next slide of the compiled pdf. "
                + "Decide whether this deck is finished as far as revealing its answers is concerned. "
                + $"{AnswerMacroReviewExclusions} "
                + "Answer PASS if every question that needs an answer has one that reveals on overlay 2 by "
                + "any of those means. "
                + "Answer FAIL only if a question that plainly needs an answer has none, or an answer is "
                + "already visible on overlay 1 in the part of the file that is not commented out, or an "
                + @"answer inside the braces would not compile, such as \ablank{3x^2} where the macro is not "
                + "already inside math mode. "
                + $"Source: \n\n {rootSourceContents}";
        }

        private static String GenerateAnswerMacroRewritePrompt(String rootSourceContents)
        {
            return "Below is the contents of a .tex file for slides of questions. Rewrite it so that "
                + "every question reveals its answer on overlay 2 of its own slide, choosing for each answer "
                + "whichever of the helpers below shows it best. "
                + "These definitions must appear verbatim in the preamble, and the deck must load tikz for "
                + "the last of them: "
                + $"\n\n{AnswerMacros.Definitions}\n\n"
                + "Leave the questions themselves unchanged - only the answers are being added. "
                + "If the deck already puts its solutions on separate slides then keep those slides "
                + "exactly as they are, the helpers are needed as well, not instead. "
                + "If a question already reveals its answer properly, leave it exactly as it is, whichever "
                + "helper it uses. "
                + $"{AnswerMacroUsageRules} "
                + $"{RewriteRequirements} Original source: \n\n {rootSourceContents}";
        }

        // the reviewer's own words are too long, and often too pedantic, to put in front of a
        // teacher unedited
        private static String SummariseReviewReasonsPrompt(IEnumerable<String> reasons)
        {
            return "An automated review of a set of question slides rejected them, and a rewrite did not "
                + "settle it. Below are the reasons the review gave, one round per block, oldest first. "
                + "Write a note for the teacher who owns the slides, at most three sentences, saying plainly "
                + "what the review thought was wrong with the way the answers are shown, and which slides or "
                + "questions it was talking about if that is clear. "
                + "If the reasons contradict each other, or look like nitpicking rather than a real problem, "
                + "say so plainly - the teacher needs to know the review may be wrong. "
                + "Reasons: \n\n"
                + String.Join("\n\n", reasons);
        }

        #endregion
    }
}
