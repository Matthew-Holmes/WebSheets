using SyntheticPDFs.Logic;

namespace SyntheticPDFs.Tests.Fakes
{
    // slide decks used across the archetype and answer macro tests. these build on
    // AnswerMacros.Definitions rather than repeating the helpers, so there is one place
    // the literal text is pinned - see AnswerMacroTests.DefinitionsAreTheAgreedText
    public static class TexFixtures
    {
        public const String DefaultBody = "What is $2 + 2$? \\ashow{4}";

        // a deck that defines the helpers, so it gets past the cheap check and on to the review
        public static String SlideDeckDefiningAnswerMacros(String body = DefaultBody) =>
            "\\documentclass{beamer}\n"
            + "\\usepackage{xcolor}\n"
            + AnswerMacros.Definitions + "\n"
            + "\\begin{document}\n"
            + body + "\n"
            + "\\end{document}";

        // a deck with no helpers at all - fails the cheap check without any LLM call
        public static String SlideDeckWithoutAnswerMacros(String body = "What is $2 + 2$?") =>
            FakeLLMService.ValidTex(body);

        // what a well behaved rewrite comes back with
        public static String RewrittenSlideDeck() =>
            SlideDeckDefiningAnswerMacros(DefaultBody);

        // a deck that already carries a note, built the way the code builds one
        public static String SlideDeckWithReviewNote(String body = DefaultBody) =>
            AnswerMacros.AddReviewNote(SlideDeckDefiningAnswerMacros(body), "an earlier note", atStart: false)!;

        public static String WorkedSolutions(String body = "Two plus two is four.") =>
            FakeLLMService.ValidTex(body);

        public static String VerifiedWorkedSolutions(String body = "Two plus two is four.") =>
            AnswerMacros.AddVerifiedMarker(WorkedSolutions(body));
    }
}
