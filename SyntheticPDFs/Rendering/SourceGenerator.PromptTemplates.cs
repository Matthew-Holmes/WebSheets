using SyntheticPDFs.Models.Content;

namespace SyntheticPDFs.Rendering
{
    public static partial class SourceGenerator
    {
        private static String Requirements => "Just provide the contents of the worked solutions .tex file, nothing else, it MUST compile first time. It must define a document class and include imports for all packages that will be used";

        // the hosting repo picks a compiler per file, and a magic comment in the first few
        // lines overrides that choice. a derived file that drops it gets built by a different
        // engine to the source it came from - which is how a deck pinned back to pdflatex,
        // or one whose non-Latin text is produced by a macro, quietly stops building
        private static String CompilerDirectiveRule => String.Join(' ',
            @"If the source below begins with a magic comment such as % !TeX program = xelatex, copy that",
            "line across verbatim, at the very top of what you produce and above the document class.",
            "It pins which compiler builds the file, so leaving it out would build this file with a",
            "different engine to the source it came from.");

        private static String RewriteRequirements => "Just provide the contents of the rewritten .tex file, nothing else, it MUST compile first time. Keep the document class and add imports for any packages the helpers need";


        #region Archetype specific requirements

        // modelled on introductoryFractionsStarters_workedSolutions.tex, so that every set of
        // worked solutions in the repository is laid out the same way. the ordering matters:
        // each starter is finished - questions, then its workings - before the next one
        // begins, so a teacher can work down the deck in the order they teach it
        internal static String QuestionSlidesWorkedSolutionRequirements => String.Join(' ',
            "Follow this structure exactly, so that every set of worked solutions in the repository looks",
            "the same.",

            "Reproduce the preamble of the original file, including its document class, its packages and the",
            "answer overlay helpers, so that the question slides you copy across still behave as they did.",

            @"Open the body with \frame{\titlepage} and then a single contents frame titled Contents.",
            @"That frame uses \small and two equal columns opened with \begin{columns}[T]. The left column is",
            @"headed \textbf{Questions and short answers} and the right column \textbf{Worked solutions}.",
            "Each column holds one itemize with one item per starter slide in the original file, written as",
            @"\item \hyperlink{q-st1}{\beamergotobutton{Starter 1}} on the left and",
            @"\item \hyperlink{work-st1}{\beamergotobutton{Starter 1}} on the right, numbering up from 1.",

            "After the contents frame, take the starters one at a time and finish each one completely before",
            "starting the next.",
            "For starter N, first reproduce that starter's slide from the original file exactly as it is,",
            @"titled Starter N, with \hypertarget{q-stN}{} on the line after the frame title, so it still",
            "shows the questions on its first overlay and the short answers on its second.",
            "Then give every question on that starter a worked solution frame of its own, in the order the",
            @"questions appear, titled Worked Solution: Starter N, Question M, with \hypertarget{work-stN}{}",
            "on the line after the frame title of the first of them.",
            "Only then move on to starter N+1.",
            "Do not group all the question slides together and all the worked solutions together. They must",
            "interleave, so that reading down the file gives starter 1, starter 1 worked solutions, starter 2,",
            "starter 2 worked solutions, and so on to the end.",

            @"Inside a worked solution frame, restate the question after \textbf{Question:}, then put the",
            @"whole of the working inside a single \awork{Solution: ...}, showing",
            @"the steps and explaining them. Note that the indication that it is the solution is inside the \awork macro",
            @"Thus the first slide of the pair only shows the question, and nothing else",
            @"\awork is what makes the frame come out as two slides: the first shows the question with the",
            "room the working will take left blank, so it can be worked through on the board, and the",
            "second shows the working itself.",
            @"Everything that is the answer goes inside that one \awork - the steps, the explanation and",
            @"the final result. Only the restated question stays outside it. Never use more than one \awork",
            @"on a frame, and never nest anything else that reveals inside it.",
            "Keep to one question per frame however short its answer is.");
        #endregion


        // The archetype says what its worked solutions have to look like, so adding a kind
        // of source does not mean finding this switch and remembering to extend it.
        private static String GenerateEnglishWorkedSolutionsPrompt(
            String rootSourceContents, SheetArchetype at)
        {
            String layout = at.WorkedSolutionsInstructions is String instructions
                ? $"{instructions} {WorkedSolutionFrameRules} "
                : String.Empty;

            return $"Below is the contents of a .tex file for {at.Description}. Typeset worked solutions in LaTeX, showing clear workings with explanations. {layout}{CompilerDirectiveRule} {Requirements} Original source: \n\n {rootSourceContents}";
        }

        // for now only use the worked solutions, if results are not good, then use the root source too!
        private static string GenerateEnglishSolutionsPrompt(String rootSourceContents, String wsolSourceContents)
        {
            return $"Below is the contents of a .tex file. It contains worked solutions, from these extract just the correct answers and produce a concise answer key for the questions. {CompilerDirectiveRule} {Requirements} Original Source \n\n {wsolSourceContents}";
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
            "On a slide that asks questions, every answer must appear on overlay 2 of that slide, so that it",
            "reveals on the next slide of the compiled pdf. Which helper does that is a matter of what reads",
            "best on the slide.",
            "A slide whose whole job is to list the answers is not a question slide. Leave it showing",
            "everything at once and put no helpers on it.",
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
            "Before writing an answer into a helper, decide whether the macro itself is already inside math",
            @"mode. It is if it sits inside $ ... $, \( ... \), \[ ... \], or an equation, align or similar",
            "environment.",
            "If it is already inside math mode, put no math delimiters in the braces at all: write",
            @"\(\frac{3}{8} = \ablank{37.5\%}\), never \(\frac{3}{8} = \ablank{$37.5\%$}\). A $ inside",
            @"\( ... \) closes the maths early, and the file will not compile - it fails with Missing $",
            "inserted or Extra }.",
            @"Escapes such as \% and commands such as \frac work in both modes, so nothing is lost by leaving",
            "the delimiters out.",
            "If instead the macro is in ordinary text, a mathematical answer does need inline math inside the",
            @"braces: write \ablank{$3x^2$}, never \ablank{3x^2}.",
            "Anything with a superscript, subscript, fraction, root, integral or Greek letter counts as mathematical.",
            @"Either way, never use \[ \], never $$ $$ and never a display environment inside the braces, since",
            "the argument is measured to reserve its space.",
            @"The argument must be a single balanced group: no blank lines, no \\, no & and nothing verbatim.",
            @"Outside math mode, escape %, &, # and _ in the answer.",
            @"Do not add size or colour commands, the helpers already apply \small and red.",
            @"Keep \ablank answers short, since it reserves the width of the answer in the question line.",
            @"Write mathematical symbols as LaTeX commands such as \implies and \approx, never as unicode characters.",
            "Only use the helpers in the body of a frame, never in a frame title or a section heading.",
            @"The preamble also defines \awork, which belongs to the worked solutions rather than to this",
            "deck. Never put it on a question slide.",
            @"Apart from that, these four are the only helpers there are. Never invent another - there is",
            @"no \blank, no \answer and no \soln - and never use one the preamble does not define.");

        // worked solutions are the answers, so nothing on them is hidden. this exists because
        // the overlay rules used to be handed to that prompt wholesale, and the model applied
        // them to the workings too - giving every worked solution a second overlay with
        // nothing new on it, and inventing macros no preamble defines
        private static String WorkedSolutionFrameRules => String.Join(' ',
            "A worked solution frame is not a question slide, and it reveals its content differently.",
            @"It uses \awork and nothing else: the question is visible from the start, and the working",
            "appears on the next slide.",
            @"Do not use \ablank, \ashow or \ashowq on a worked solution frame, do not use the ans,",
            @"ansfill or anslab TikZ styles, and do not use \pause, \onslide, \alt or an overlay",
            @"specification such as <2-> of your own. \awork already does all of it.",
            "Each worked solution frame must come out as exactly two slides in the compiled pdf - the",
            "question with space to write in, then the working.",
            "Use only commands that the preamble defines or that LaTeX and beamer already provide. Never",
            @"invent a macro - there is no \blank, no \answer and no \soln.",
            "The starter slides you copy across from the original file are the one exception. Copy each of",
            @"them exactly as it is, keeping the helpers it already uses, and never put \awork on one of",
            "them - it belongs only to the worked solution frames you write.");

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
            "A deck may also carry a slide whose whole job is to list the answers, however it is titled -",
            "Answers, Solutions, Final answers and so on. That slide is meant to show everything at once with",
            "nothing hidden, which is deliberate and useful. Never fail a deck because such a slide reveals",
            "its answers immediately, and never fail one because an answer appears both held back on its",
            "question slide and again in full on a later answers slide.",
            @"A helper used inside existing math mode, such as $x = \ablank{52}$, is correct, and the braces do",
            "not need dollar signs of their own there.",
            "Numbering questions by hand, rather than with an enumerate, is fine.",
            @"The preamble defines \awork as well, which is used by the worked solutions rather than by",
            "this deck. Never fail a deck because it does not use it.",
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
                + "Answer FAIL only if a question that plainly needs an answer has none, or a question "
                + "slide gives its own answer away on overlay 1 instead of holding it back to overlay 2, or an "
                + "answer inside the braces would not compile. That happens either when an answer needs "
                + @"math mode and has none, as in \ablank{3x^2} in ordinary text, or when it opens math mode "
                + @"again inside a macro that is already in maths, as in \(x = \ablank{$5$}\). "
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
                + $"{CompilerDirectiveRule} "
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
