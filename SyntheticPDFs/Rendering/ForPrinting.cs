using System.Text;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Rendering
{
    // The printable version of a deck of starters.
    //
    // A deck is written to be projected: the questions go up on the board, and the
    // answers arrive on the next overlay of the same slide. That is no use to a pupil who
    // was away, or who cannot see the board, or who is working from paper. What they want
    // is the questions as they stand before anything is revealed, small enough that one
    // page cuts into four and one piece goes to each of four pupils.
    //
    // So this makes a deck that says two things to LaTeX and changes nothing else:
    //
    //   every slide is asked for at its first step only, with an overlay of <1>, which is
    //     beamer's own way of saying "just this much of the slide" - so whatever the deck
    //     uses to reveal an answer, be it one of the answer helpers, a \pause, or a
    //     picture that draws itself in, none of it has happened yet;
    //   four copies of each slide are written out in a row, and pgfpages is told to put
    //     four slides on a page in a two by two grid with a line round each - so a page
    //     is four copies of one slide and cuts into four question sheets.
    //
    // Every slide, without exception. That is the whole of the rule and it is worth
    // stating plainly, because the grid depends on it: four copies to a page means one
    // page is one slide, so page 5 of the pdf is slide 5 of the deck and a teacher can
    // print the two starters they want by asking for two pages. A slide left out would be
    // harmless, but a slide that reached the page *without* being repeated would put
    // every page after it half a starter out of step - which is exactly what happened
    // while this only recognised frames written one of the two ways beamer allows.
    //
    // So a title page is repeated like anything else rather than dropped. It costs one
    // page and it keeps the promise above, where dropping it saved a page and cost the
    // promise.
    //
    // No model is asked anything. Every byte outside those two changes is the byte that
    // was there before, which matters for the same reason it matters in
    // RetrieveAndConnect: a model handed a beamer deck to reproduce will quietly reflow a
    // picture or lose an overlay, and the failure turns up in a lesson rather than in a
    // build. There is nothing here a model could do better either - what a slide says on
    // paper is what it said on the board.
    internal static class ForPrinting
    {
        // copies of each slide on one page, which is also how many logical pages pgfpages
        // is being asked to fit on a physical one
        internal const int Copies = 4;

        // The margin left around each slide. Small on purpose: these are cut up and
        // handed out, so paper spent on a border is paper not spent on the question.
        internal const String Margin = "1mm";

        // bumped when the rules below change in a way that alters the output, which makes
        // every printable deck built by the old ones stale
        internal const int Version = 3;

        #region Laying a deck out

        // The deck laid out for printing, or null when there was nothing here this could
        // lay out - no document body, or no frames inside it - in which case the caller
        // falls back to a copy that says so.
        internal static String? Render(String texSource, String builtFrom)
        {
            String source = texSource.Replace("\r\n", "\n");

            int opens = IndexOfCode(source, DocumentOpen, 0);

            if (opens < 0) { return null; }

            int bodyAt = opens + DocumentOpen.Length;

            int closes = LastIndexOfCode(source, DocumentClose);

            if (closes < bodyAt) { return null; }

            String preamble = source[..opens];
            String body = source[bodyAt..closes];
            String tail = source[closes..];

            String? repeated = FourCopiesOfEachSlide(body, out int slides);

            if (repeated is null) { return null; }

            if (!preamble.EndsWith('\n')) { preamble += "\n"; }

            return Provenance(builtFrom, slides) + "\n"
                + preamble
                + Layout + "\n"
                + DocumentOpen + repeated + tail;
        }

        // Neither a document body nor a single frame was found, so the printable version
        // is the deck itself. Better than no file at all: whoever clicks it gets the
        // lesson they were after, and the header says plainly why it is not laid out.
        internal static String Unchanged(String texSource, String builtFrom) =>
            NothingToLayOut(builtFrom) + "\n" + texSource.Replace("\r\n", "\n");

        private const String DocumentOpen = @"\begin{document}";
        private const String DocumentClose = @"\end{document}";

        private const String FrameOpen = @"\begin{frame}";
        private const String FrameClose = @"\end{frame}";

        // The other way beamer lets a deck write a slide, and not a rare one: it is the
        // form every beamer template uses for a title page, as \frame{\titlepage}. A deck
        // whose title page is written that way and whose starters are written as
        // environments uses both, so both have to be found or the grid slips.
        private const String FrameCommand = @"\frame";

        // Where one slide starts and ends, whichever of the two ways it is written.
        private readonly record struct Slide(int Start, int End);

        // Each slide written out four times over, asked for at its first step. Everything
        // between the slides - a comment, a \section, a stray macro call - is left where
        // it was and written out once, since it is the deck's own structure rather than
        // something that reaches the page.
        private static String? FourCopiesOfEachSlide(String body, out int slides)
        {
            StringBuilder sb = new(body.Length * Copies);

            slides = 0;

            int copied = 0;
            int at = 0;

            while (NextSlide(body, at) is Slide slide)
            {
                String one = AtItsFirstStep(body[slide.Start..slide.End]);

                sb.Append(body, copied, slide.Start - copied);

                for (int copy = 0; copy < Copies; copy++)
                {
                    if (copy > 0) { sb.Append('\n'); }

                    sb.Append(one);
                }

                slides++;

                copied = slide.End;
                at = slide.End;
            }

            if (slides == 0) { return null; }

            sb.Append(body, copied, body.Length - copied);

            return sb.ToString();
        }

        // The next slide after `from`, written either way, whichever comes first. Null
        // once there are no more - and also when what is there cannot be read, since a
        // slide that does not close is a deck that would not have compiled anyway and
        // guessing where it ended would be worse than leaving it alone.
        private static Slide? NextSlide(String body, int from)
        {
            int environment = IndexOfCode(body, FrameOpen, from);
            int command = IndexOfTheFrameCommand(body, from);

            if (environment < 0 && command < 0) { return null; }

            if (environment >= 0 && (command < 0 || environment < command))
            {
                int closes = IndexOfCode(body, FrameClose, environment + FrameOpen.Length);

                if (closes < 0) { return null; }

                return new Slide(environment, closes + FrameClose.Length);
            }

            int cursor = SkipStepAndOptions(body, command + FrameCommand.Length);

            if (cursor < 0) { return null; }

            if (TexArguments.ReadGroup(body, ref cursor) is null) { return null; }

            // ReadGroup leaves the cursor after the closing brace
            return new Slide(command, cursor);
        }

        // \frame must not match \frametitle, \framesubtitle or \framebox, and what follows
        // it has to be the slide rather than nothing at all.
        private static int IndexOfTheFrameCommand(String source, int from)
        {
            int at = from;

            while (true)
            {
                int found = IndexOfCode(source, FrameCommand, at);

                if (found < 0) { return -1; }

                at = found + FrameCommand.Length;

                if (at < source.Length && Char.IsLetter(source[at])) { continue; }

                int opens = TexArguments.SkipSpace(source, at);

                if (opens >= source.Length) { return -1; }

                if (source[opens] is '{' or '<' or '[') { return found; }
            }
        }

        // Steps over a step specification and any options in front of the slide itself,
        // so that what is read after them is the slide's own body.
        private static int SkipStepAndOptions(String source, int at)
        {
            while (true)
            {
                at = TexArguments.SkipSpace(source, at);

                if (at >= source.Length) { return -1; }

                char opens = source[at];

                if (opens != '<' && opens != '[') { return at; }

                int closes = source.IndexOf(opens == '<' ? '>' : ']', at);

                if (closes < 0) { return -1; }

                at = closes + 1;
            }
        }

        // An overlay of <1>, which is beamer's own way of asking for the first step of a
        // slide and nothing after it. A slide that already carries a step of its own has
        // it replaced: a printed question sheet showing the answers would be worse than
        // one showing a slide its author meant to appear later.
        internal static String AtItsFirstStep(String slide)
        {
            const String step = "<1>";

            // a slide opens with one macro or the other, and it is only that macro that
            // has anything added to it
            int after = slide.StartsWith(FrameOpen, StringComparison.Ordinal)
                ? FrameOpen.Length
                : FrameCommand.Length;

            int cursor = TexArguments.SkipSpace(slide, after);

            if (cursor < slide.Length && slide[cursor] == '<')
            {
                int ends = slide.IndexOf('>', cursor);

                // a step specification that never closes is a broken slide, and guessing
                // where it was meant to end would be worse than leaving it alone
                if (ends < 0) { return slide; }

                return slide[..after] + step + slide[(ends + 1)..];
            }

            return slide[..after] + step + slide[after..];
        }

        #endregion

        #region What is added to the preamble

        // Added just before \begin{document}, so it has the last word over whatever the
        // deck set for itself.
        private static String Layout => String.Join('\n',
            "% ---------------------------------------------------------------",
            "% Added so this deck prints four slides to a page. Nothing above",
            "% this line was changed.",
            "% ---------------------------------------------------------------",
            @"\usepackage{pgfpages}",
            "",
            $"% {Copies} slides to a sheet of A4, two by two, with a thin line round each",
            "% so that the page can be cut up.",
            "%",
            $"% Each slide keeps a margin of {Margin} around it and no more, so the four fill",
            "% the sheet and the pieces they cut into are as big as the paper allows. Only",
            "% one direction actually comes out that tight: a slide is a different shape",
            "% from a quarter of a sheet of A4, so it is scaled to fit and the slack it",
            "% cannot use is left along whichever pair of edges it did not fill.",
            $@"\pgfpagesuselayout{{4 on 1}}[a4paper,landscape,border shrink={Margin}]",
            @"\pgfpageslogicalpageoptions{1}{border code=\pgfusepath{stroke}}",
            @"\pgfpageslogicalpageoptions{2}{border code=\pgfusepath{stroke}}",
            @"\pgfpageslogicalpageoptions{3}{border code=\pgfusepath{stroke}}",
            @"\pgfpageslogicalpageoptions{4}{border code=\pgfusepath{stroke}}",
            "",
            "% There is nothing on paper to click, and a slide announcing a new section",
            "% would sit between two copies of the same question and put every page after",
            "% it out of step with the grid.",
            @"\setbeamertemplate{navigation symbols}{}",
            @"\AtBeginPart{}",
            @"\AtBeginSection{}",
            @"\AtBeginSubsection{}",
            @"\AtBeginSubsubsection{}");

        #endregion

        #region What it says about itself

        private static readonly Regex VersionLine =
            new(@"printing rules\s+version\s+(\d+)", RegexOptions.Compiled);

        // Whether a printable deck already in the repository was made by the rules in
        // force now. A file that says nothing about how it was made cannot be shown to be
        // current, so it is not.
        internal static bool MatchesCurrentRules(String texSource)
        {
            Match version = VersionLine.Match(texSource);

            return version.Success && int.Parse(version.Groups[1].Value) == Version;
        }

        private static String Provenance(String builtFrom, int slides) =>
            String.Join('\n',
                "% ================================================================",
                $"% The printable version of {builtFrom}",
                "%",
                "% This is the same deck, laid out to be handed out rather than put on",
                "% the board. Every slide is shown as it stands before any answer is",
                $"% revealed, and {Copies} copies of it go on one page of A4, two by two, so",
                "% that a printed page cuts into four question sheets.",
                "%",
                "% Every slide of the deck is here, the title page included, and each takes",
                "% exactly one page. So page 1 is the first slide, page 2 the second, and",
                "% printing the pages you want is how you print the starters you want.",
                "%",
                $"%   made from           {builtFrom}",
                $"%   pages on the sheet  {slides}, one per slide of the deck",
                $"%   copies of each      {Copies}, two by two on A4 landscape",
                $"%   printing rules      version {Version}",
                "%",
                "% Nothing was reworded, nothing was rewritten and nothing was left out.",
                "% Each slide is the slide from the deck, asked for at its first step,",
                "% which is how the answers stay hidden.",
                "%",
                "% Please don't edit this file. It is written again from the one it was",
                "% made from whenever that changes, so an edit here would be lost - edit",
                "% that file instead and this one follows.",
                "% ================================================================");

        private static String NothingToLayOut(String builtFrom) =>
            String.Join('\n',
                "% ================================================================",
                $"% The printable version of {builtFrom}",
                "%",
                "% This is a copy of that deck with nothing changed, because nothing in it",
                "% reads as a slide: there are no frames between the start and the end of",
                "% the document for this to lay out four to a page.",
                "%",
                $"%   made from           {builtFrom}",
                "%   slides on the sheet none were found",
                $"%   printing rules      version {Version}",
                "%",
                "% Please don't edit this file. It is written again from the one it was",
                "% made from whenever that changes, so an edit here would be lost - edit",
                "% that file instead and this one follows.",
                "% ================================================================");

        #endregion

        #region Finding something that is not inside a comment

        // A \begin{frame} on a commented out line is not a frame, and a deck with an old
        // slide commented out is a normal thing to meet.
        private static int IndexOfCode(String source, String what, int from)
        {
            int at = from;

            while (true)
            {
                int found = source.IndexOf(what, at, StringComparison.Ordinal);

                if (found < 0) { return -1; }

                if (!IsCommentedOut(source, found)) { return found; }

                at = found + what.Length;
            }
        }

        private static int LastIndexOfCode(String source, String what)
        {
            int last = -1;
            int at = 0;

            while (true)
            {
                int found = IndexOfCode(source, what, at);

                if (found < 0) { return last; }

                last = found;
                at = found + what.Length;
            }
        }

        private static bool IsCommentedOut(String source, int at)
        {
            int lineStart = source.LastIndexOf('\n', at) + 1;

            for (int i = lineStart; i < at; i++)
            {
                // an escaped percent is a character, not the start of a comment
                if (source[i] == '\\') { i++; continue; }

                if (source[i] == '%') { return true; }
            }

            return false;
        }

        #endregion
    }
}
