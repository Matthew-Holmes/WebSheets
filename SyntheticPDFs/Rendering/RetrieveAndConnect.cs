using System.Text;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Rendering
{
    // The "Retrieve and Connect" version of a deck of starters.
    //
    // Some schools have their own name for a starter and expect to see it on the board.
    // The deck is the same deck - the same questions, the same answers, the same
    // overlays - so this is not a rewrite, it is a rename: the only thing it changes is
    // the wording of the titles, and it changes them by editing the file rather than by
    // asking a model to write it out again.
    //
    // That matters more than it sounds. A model asked to reproduce a beamer deck with
    // one word changed will also quietly reflow a TikZ picture or drop an overlay
    // specification, and the failure is a slide that comes out wrong in a lesson rather
    // than a build that goes red. Editing the titles in place cannot do that: every byte
    // outside a title is the byte that was there before.
    //
    // Three rules, in the order they are applied to one title:
    //
    //   a title that is a starter's own title loses the number after the word, since
    //     the school's name is the title and there is nothing to number;
    //   unless what follows points at a particular question, in which case the number
    //     stays, or "Starter 3 -- Q2" would stop saying which starter;
    //   anywhere else the word appears in a title it is simply renamed, numbers and
    //     all - which is what leaves the contents slide of a set of worked solutions
    //     able to list them one by one.
    internal static class RetrieveAndConnect
    {
        internal const String Name = "Retrieve and Connect";

        // bumped when the rules below change in a way that alters the output, which makes
        // every variant built by the old ones stale
        internal const int Version = 1;

        #region Rewriting a deck

        // The deck with its titles renamed, or null when there was nothing here this
        // recognised - no title mentions a starter at all - in which case the caller
        // falls back to asking a model.
        internal static String? Rewrite(String texSource, String builtFrom)
        {
            String source = texSource.Replace("\r\n", "\n");

            int renamed = 0;

            // the title of the deck and the title of each slide: the places a starter is
            // named rather than referred to, so the number goes
            source = InTitles(source, FrameTitle, own: true, ref renamed);
            source = InArgument(source, @"\frametitle", own: true, ref renamed);
            source = InArgument(source, @"\framesubtitle", own: true, ref renamed);
            source = InArgument(source, @"\title", own: true, ref renamed);

            // the buttons on a contents slide, which point at one starter among several
            source = InArgument(source, @"\beamergotobutton", own: false, ref renamed);

            if (renamed == 0) { return null; }

            return Provenance(builtFrom, "by renaming the titles in it") + "\n" + source;
        }

        // A file a model wrote for us, stamped so the next pass can tell how old its
        // rules are. Kept honest about which route produced it, since the two are not
        // equally trustworthy and whoever opens the file deserves to know which they
        // are looking at.
        internal static String FromAModel(String texSource, String builtFrom) =>
            Provenance(builtFrom, "by a language model, since no starter titles were found")
            + "\n" + texSource.Replace("\r\n", "\n");

        // Neither route could find a title to change, so the variant is the deck itself.
        // Better than no variant at all: whoever clicks it gets the lesson they were
        // after, and the header says plainly why it reads the same as the original.
        internal static String Unchanged(String texSource, String builtFrom) =>
            Provenance(builtFrom, "with nothing changed, since no title in it named a starter")
            + "\n" + texSource.Replace("\r\n", "\n");

        #endregion

        #region Finding a title

        private const String FrameTitle = @"\begin{frame}";

        // Every title of a frame opened with \begin{frame}{...}, optional arguments and
        // all. The group has to be on the same line as the \begin{frame}, which is what
        // stops a frame with no title at all - whose body may perfectly well open with a
        // group of its own - having its first line read as one.
        private static String InTitles(
            String source, String macro, bool own, ref int renamed)
        {
            return Scan(source, macro, own, sameLine: true, ref renamed);
        }

        // The argument of a macro that takes one. Nothing else can be there, so a line
        // break in front of it is only unusual formatting rather than a reason to doubt
        // what it is.
        private static String InArgument(
            String source, String macro, bool own, ref int renamed)
        {
            return Scan(source, macro, own, sameLine: false, ref renamed);
        }

        private static String Scan(
            String source, String macro, bool own, bool sameLine, ref int renamed)
        {
            StringBuilder sb = new(source.Length);

            int copied = 0;
            int at = 0;
            int here = 0;

            while (true)
            {
                int found = source.IndexOf(macro, at, StringComparison.Ordinal);

                if (found < 0) { break; }

                int after = found + macro.Length;

                at = after;

                // \title must not match \titlepage, nor \frametitle a longer name
                if (after < source.Length && Char.IsLetter(source[after])) { continue; }

                (int Start, int End)? group = ArgumentAt(source, after, sameLine);

                if (group is null) { continue; }

                String title = source[group.Value.Start..group.Value.End];

                if (!TheWord.IsMatch(title)) { continue; }

                sb.Append(source, copied, group.Value.Start - copied);
                sb.Append(Retitle(title, own));

                copied = group.Value.End;
                at = group.Value.End;

                here++;
            }

            if (here == 0) { return source; }

            renamed += here;

            sb.Append(source, copied, source.Length - copied);

            return sb.ToString();
        }

        // Where the text of the argument starts and ends, having stepped over any
        // optional arguments in front of it. Returns null when there is no group there,
        // which is how a \begin{frame} with no title is left alone.
        private static (int Start, int End)? ArgumentAt(String source, int from, bool sameLine)
        {
            int cursor = SkipOptional(source, from);

            if (cursor < 0) { return null; }

            cursor = TexArguments.SkipSpace(source, cursor);

            // measured over the whole run, options included, so a line break anywhere
            // between the \begin{frame} and the group counts
            if (sameLine && CrossesALine(source, from, cursor)) { return null; }

            int before = cursor;

            if (TexArguments.ReadGroup(source, ref cursor) is null) { return null; }

            // ReadGroup leaves the cursor after the closing brace
            return (before + 1, cursor - 1);
        }

        private static int SkipOptional(String source, int at)
        {
            while (true)
            {
                int next = TexArguments.SkipSpace(source, at);

                if (next >= source.Length || source[next] != '[') { return at; }

                int close = source.IndexOf(']', next);

                // an option list that never closes is a broken file, and guessing where
                // it was meant to end would be worse than leaving the frame alone
                if (close < 0) { return -1; }

                at = close + 1;
            }
        }

        private static bool CrossesALine(String source, int from, int to) =>
            source.AsSpan(from, to - from).IndexOf('\n') >= 0;

        #endregion

        #region Renaming one title

        private static readonly Regex TheWord =
            new(@"\bstarters?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex OpensWithTheWord =
            new(@"^\s*starters?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ItsOwnNumber =
            new(@"^(\s*starters?)\s*\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "Q2", "Question 3" - a title pointing at one question of one starter, which is
        // only useful while it still says which starter
        private static readonly Regex PointsAtAQuestion =
            new(@"\bq(?:uestion)?s?\s*\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // what is left when the number was the whole of the rest of the title
        private static readonly Regex TrailingSeparator =
            new(@"^(\s*starters?)\s*[-–—:,.]+\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static String Retitle(String title, bool own)
        {
            if (own && OpensWithTheWord.IsMatch(title) && !PointsAtAQuestion.IsMatch(title))
            {
                title = ItsOwnNumber.Replace(title, "$1", 1);
                title = TrailingSeparator.Replace(title, "$1", 1);
            }

            return TheWord.Replace(title, m =>
                m.Value.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? Name + "s" : Name);
        }

        #endregion

        #region What it says about itself

        private static readonly Regex VersionLine =
            new(@"retitling rules\s+version\s+(\d+)", RegexOptions.Compiled);

        // Whether a variant already in the repository was made by the rules in force now.
        // A file that says nothing about how it was made cannot be shown to be current,
        // so it is not.
        internal static bool MatchesCurrentRules(String texSource)
        {
            Match version = VersionLine.Match(texSource);

            return version.Success && int.Parse(version.Groups[1].Value) == Version;
        }

        private static String Provenance(String builtFrom, String how) =>
            String.Join('\n',
                "% ================================================================",
                $"% The {Name} version of {builtFrom}",
                "%",
                "% This is the same deck as the one above, made " + how + ".",
                "% Every slide that was a numbered starter is now titled",
                $"% \"{Name}\", and the word is renamed wherever else a title",
                "% used it. Nothing outside a title has been changed.",
                "%",
                $"%   made from           {builtFrom}",
                $"%   retitling rules     version {Version}",
                "%",
                "% Please don't edit this file. It is written again from the one it was",
                "% made from whenever that changes, so an edit here would be lost - edit",
                "% that file instead and this one follows.",
                "% ================================================================");

        #endregion
    }
}
