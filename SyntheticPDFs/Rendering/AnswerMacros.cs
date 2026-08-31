using SyntheticPDFs.Models.Content;
using System.Text;

namespace SyntheticPDFs.Rendering
{
    // question slides reveal their answers on the next overlay rather than in a separate
    // solutions pdf, which only works if the deck defines and uses these helpers. there is
    // more than one way to reveal an answer and none of them is the right one - what matters
    // is that every answer appears on overlay 2, however that reads best on the slide
    internal static class AnswerMacros
    {
        // built line by line rather than as one literal so the constants don't pick up
        // whatever line endings this file happens to be saved with

        internal static String Ablank => String.Join('\n',
            @"\newcommand{\ablank}[1]{%",
            @"  \alt<2>{\textcolor{red}{#1}}{\underline{\phantom{#1}}}%",
            @"}");

        internal static String Ashow =>
            @"\newcommand{\ashow}[1]{\uncover<2->{\textcolor{red}{\small #1}}}";

        internal static String Ashowq =>
            @"\newcommand{\ashowq}[1]{\alt<2->{\textcolor{red}{\small #1}}{?}}";

        // The one helper that belongs to the worked solutions rather than to the deck of
        // questions. A worked solution frame becomes the question with the room its answer
        // will take left empty - to be written in on the board - and then the answer
        // itself on the next slide.
        //
        // \uncover rather than \only, because the space has to be reserved: the point is
        // that the answer appears exactly where it was being written, and a frame that
        // reflowed between the two slides would defeat that.
        //
        // Black rather than the red the question slides use. On a question slide red says
        // "this was hidden a moment ago"; on a worked solution the whole frame is the
        // answer, so colouring it would say nothing and read as a mistake.
        internal static String Awork => String.Join('\n',
            @"\newlength{\aworkpad}",
            @"\setlength{\aworkpad}{8mm}",
            @"\newcommand{\awork}[1]{%",
            @"  \par\vspace{\aworkpad}%",
            @"  \uncover<2->{#1}%",
            @"  \par\vspace{\aworkpad}%",
            @"}");

        // for an answer that is better drawn on to a diagram than written out. the styles go
        // on nodes and paths inside a picture the deck already has, so they are checked as
        // one block rather than as a command
        internal static String DiagramStyles => String.Join('\n',
            @"\usetikzlibrary{overlay-beamer-styles}",
            @"\tikzset{",
            @"  ans/.style     = {red, thick, visible on=<2->},",
            @"  ansfill/.style = {red!15, visible on=<2->},",
            @"  anslab/.style  = {red, font=\tiny, visible on=<2->},",
            @"}");

        // what a deck missing the helpers gets handed to paste in, banners and all
        internal static String Definitions => String.Join('\n',
            "% ================================================================",
            "% Answer-overlay helpers",
            "% ================================================================",
            Ablank,
            Ashow,
            Ashowq,
            "",
            @"% Answers drawn straight on to a TikZ diagram, revealed on overlay 2 like \ashow.",
            "% Space is reserved on overlay 1, so the diagram does not move between slides.",
            DiagramStyles,
            "",
            "% Used by the worked solutions rather than by this deck, so that each worked",
            "% solution is first a question with room to write in and then the answer.",
            Awork);

        // written into the worked solutions once a deck has been checked, so we don't pay
        // to ask again every pass. a human editing the deck makes the worked solutions
        // stale, they get removed, and the check comes back with them
        internal static String VerifiedMarker => "% Root file used ashow macros";

        // left in a deck that has been given a review note slide, so that a later review
        // doesn't stack a second note on top of the first
        internal static String ReviewNoteMarker => "% answer macro review note";

        // every one of them has to be there verbatim - that is the cheap necessary condition that
        // settles most decks before the LLM is asked anything. line endings are the only
        // difference forgiven: a deck that has retyped the helpers slightly gets rewritten,
        // which is the safe way round to be wrong
        internal static bool AreDefined(String texSource)
        {
            String normalised = Normalise(texSource);

            return normalised.Contains(Ablank, StringComparison.Ordinal)
                && normalised.Contains(Ashow, StringComparison.Ordinal)
                && normalised.Contains(Ashowq, StringComparison.Ordinal)
                && normalised.Contains(Awork, StringComparison.Ordinal)
                && normalised.Contains(DiagramStyles, StringComparison.Ordinal);
        }

        // a whole line, so the marker can't be matched inside a sentence that mentions it
        internal static bool IsMarkedVerified(String texSource)
        {
            return Normalise(texSource)
                .Split('\n')
                .Any(line => line.Trim() == VerifiedMarker);
        }

        internal static bool HasReviewNote(String texSource)
        {
            return Normalise(texSource)
                .Split('\n')
                .Any(line => line.Trim() == ReviewNoteMarker);
        }

        // goes first so it survives anything appended later, and a leading '%' keeps the
        // file passing IsValidTex
        internal static String AddVerifiedMarker(String texSource)
        {
            if (IsMarkedVerified(texSource)) { return texSource; }

            return VerifiedMarker + "\n" + texSource;
        }

        // Splices a note frame into the deck, spelling out that the review and the fixer
        // never agreed. This is done here rather than by asking a model to hand the deck
        // back with a slide added: the note is the only way a person finds out, so it must
        // not depend on a model reproducing hundreds of lines byte for byte. Nothing outside
        // the inserted frame is touched, and the deck's own line endings are kept.
        //
        // Returns null when there is no document body to put it in, which means the deck
        // could not have compiled anyway.
        internal static String? AddReviewNote(String texSource, String summary, bool atStart)
        {
            if (HasReviewNote(texSource)) { return texSource; }

            String newline = texSource.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

            String frame = ReviewNoteFrame(summary, newline);

            if (atStart)
            {
                const String open = @"\begin{document}";

                int at = texSource.IndexOf(open, StringComparison.Ordinal);

                if (at < 0) { return null; }

                int after = at + open.Length;

                return texSource[..after] + newline + frame + texSource[after..];
            }

            const String close = @"\end{document}";

            int end = texSource.LastIndexOf(close, StringComparison.Ordinal);

            if (end < 0) { return null; }

            return texSource[..end] + frame + newline + texSource[end..];
        }

        private static String ReviewNoteFrame(String summary, String newline)
        {
            return String.Join(newline,
                ReviewNoteMarker,
                @"\begin{frame}",
                @"\frametitle{Automated check: answers may need attention}",
                "The answers on these slides may have unresolved formatting issues. An automated",
                "review did not accept them, and rewriting them did not settle it.",
                "",
                Escape(summary).Replace("\n", newline),
                "",
                "Please check these slides and either correct them or raise an issue.",
                @"\end{frame}",
                "");
        }

        // the summary is prose from a model, so anything in it that LaTeX would read as
        // markup has to be neutralised - a stray % comments out the rest of the line, and an
        // & looks like a table cell
        private static String Escape(String text)
        {
            StringBuilder sb = new StringBuilder(text.Length);

            foreach (char c in Normalise(text))
            {
                switch (c)
                {
                    case '\\': sb.Append(@"\textbackslash{}"); break;
                    case '{': sb.Append(@"\{"); break;
                    case '}': sb.Append(@"\}"); break;
                    case '$': sb.Append(@"\$"); break;
                    case '&': sb.Append(@"\&"); break;
                    case '#': sb.Append(@"\#"); break;
                    case '%': sb.Append(@"\%"); break;
                    case '_': sb.Append(@"\_"); break;
                    case '^': sb.Append(@"\textasciicircum{}"); break;
                    case '~': sb.Append(@"\textasciitilde{}"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        private static String Normalise(String texSource)
        {
            return texSource.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
