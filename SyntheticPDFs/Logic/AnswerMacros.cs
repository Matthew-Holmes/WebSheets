namespace SyntheticPDFs.Logic
{
    // question slides reveal their answers on a second overlay rather than in a separate
    // solutions pdf, which only works if the deck defines and uses these helpers
    internal static class AnswerMacros
    {
        // built line by line rather than as one literal so the constants don't pick up
        // whatever line endings this file happens to be saved with

        internal static String Ablank => String.Join('\n',
            @"\newcommand{\ablank}[1]{%",
            @"  \alt<2>{\textcolor{red}{\underline{#1}}}{\underline{\phantom{#1}}}%",
            @"}");

        internal static String Ashow =>
            @"\newcommand{\ashow}[1]{\uncover<2->{\textcolor{red}{\small #1}}}";

        internal static String Ashowq =>
            @"\newcommand{\ashowq}[1]{\alt<2->{\textcolor{red}{\small #1}}{?}}";

        // what a deck missing the helpers gets handed to paste in, banner and all
        internal static String Definitions => String.Join('\n',
            "% ================================================================",
            "% Answer-overlay helpers",
            "% ================================================================",
            Ablank,
            Ashow,
            Ashowq);

        // written into the worked solutions once a deck has been checked, so we don't pay
        // to ask again every pass. a human editing the deck makes the worked solutions
        // stale, they get removed, and the check comes back with them
        internal static String VerifiedMarker => "% Root file used ashow macros";

        // the definitions have to be there verbatim - that is the cheap necessary condition
        // that settles most decks before the LLM is asked anything. line endings are the
        // only difference forgiven: a deck that has retyped the helpers slightly gets
        // rewritten, which is the safe way round to be wrong
        internal static bool AreDefined(String texSource)
        {
            String normalised = Normalise(texSource);

            return normalised.Contains(Ablank, StringComparison.Ordinal)
                && normalised.Contains(Ashow, StringComparison.Ordinal)
                && normalised.Contains(Ashowq, StringComparison.Ordinal);
        }

        // a whole line, so the marker can't be matched inside a sentence that mentions it
        internal static bool IsMarkedVerified(String texSource)
        {
            return Normalise(texSource)
                .Split('\n')
                .Any(line => line.Trim() == VerifiedMarker);
        }

        // goes first so it survives anything appended later, and a leading '%' keeps the
        // file passing IsValidTex
        internal static String AddVerifiedMarker(String texSource)
        {
            if (IsMarkedVerified(texSource)) { return texSource; }

            return VerifiedMarker + "\n" + texSource;
        }

        private static String Normalise(String texSource)
        {
            return texSource.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
