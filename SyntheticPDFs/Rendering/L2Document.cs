using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models.Content;
using System.Text;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Rendering
{
    // Assembles a translated sheet from the body a model wrote and the preamble the
    // generator owns.
    //
    // The model is asked for the body alone. Everything that has to be exactly right for
    // the file to compile at all - the compiler directive, the language setup, the font
    // faces, the macros - is written here and wrapped around it. That removes an entire
    // class of failure: a model cannot get a preamble subtly wrong if it never writes one.
    internal static class L2Document
    {
        internal static String Assemble(
            String body,
            String documentClass,
            String originalPreamble,
            String title,
            L2ColourOptions colours,
            LanguageProfile language,
            String builtFrom,
            String vocabularyKey,
            String? fallbackFont = null)
        {
            StringBuilder sb = new();

            sb.AppendLine(L2Macros.CompilerDirective);
            sb.AppendLine(L2Macros.ProvenanceBlock(
                title, colours, language, builtFrom, vocabularyKey,
                isKey: false, fallbackFont: fallbackFont));
            sb.AppendLine();

            sb.AppendLine(documentClass);

            if (originalPreamble.Length > 0)
            {
                sb.AppendLine(OriginalPreambleHeader);
                sb.AppendLine(originalPreamble);
                sb.AppendLine();
            }

            sb.AppendLine(L2Macros.LanguagePreamble(language, fallbackFont));
            sb.AppendLine(L2Macros.Definitions(colours));
            sb.AppendLine();
            sb.Append(body);

            return sb.ToString();
        }

        // Plain words, for a teacher who opens the file to fix something rather than for
        // anybody who knows how the generator is put together.
        private const String OriginalPreambleHeader =
            "% ================================================================\n"
            + "% Copied from the English sheet this was translated from, so that\n"
            + "% anything it sets up for itself still works here\n"
            + "% ================================================================";

        #region Reading the original's preamble

        private static readonly Regex DocumentClassLine =
            new(@"^[ \t]*\\documentclass.*$", RegexOptions.Multiline | RegexOptions.Compiled);

        // The translated sheet keeps the original's class, so a deck stays a deck. Read
        // from the English source rather than asked of the model, for the same reason as
        // the rest of the preamble.
        internal static String DocumentClassOf(String source)
        {
            Match match = DocumentClassLine.Match(StripComments(source));

            // an article is the safe assumption: it is what most of the sheets are, and a
            // deck that lost its class would fail loudly rather than quietly
            return match.Success ? match.Value.Trim() : @"\documentclass[12pt]{article}";
        }

        // xcolor and babel come from our own block, so dropping the original's copies
        // avoids an option clash - loading a package twice with different options is a
        // hard error
        private static readonly String[] OursAlready = { "xcolor", "babel", "fontspec", "polyglossia" };

        // Everything the original set up for itself, carried across whole.
        //
        // This used to be a list of the four kinds of line that seemed to matter -
        // packages, tikz libraries, colours, theorems. That was wrong in the way an
        // allowlist usually is: a sheet defines macros of its own too, and a slide deck
        // now always does, because the answer overlay helpers that reveal its answers are
        // written into its preamble. The translation kept using them and no longer
        // defined them, so nine of the ten parallel texts in the repository stopped
        // compiling with "Undefined control sequence".
        //
        // So the rule is the other way round now: keep the preamble, and drop only what
        // we are certain to say again ourselves. A line we have not thought of is carried
        // rather than lost, which is the failure worth having.
        internal static String PreambleOf(String source)
        {
            List<String> lines = source.Replace("\r\n", "\n").Split('\n').ToList();

            int start = lines.FindIndex(l => DocumentClassLine.IsMatch(StripComments(l)));

            int end = lines.FindIndex(l =>
                StripComments(l).Contains(@"\begin{document}", StringComparison.Ordinal));

            if (end < 0) { end = lines.Count; }

            // a source with no class of its own is odd but not a reason to lose its
            // macros, so everything before the document begins is its preamble
            IEnumerable<String> preamble = lines.Take(end).Skip(start + 1);

            return String.Join('\n', preamble.Where(Keep)).Trim('\n');
        }

        // Whether this line is the original's to give us. Anything we provide ourselves is
        // dropped: loading a package twice with different options is a hard error, and
        // redefining one of the eal helpers would clash with the block that defines them.
        private static bool Keep(String line)
        {
            String bare = StripComments(line);

            if (bare.Contains(@"\usepackage", StringComparison.Ordinal)
                && OursAlready.Any(p =>
                    bare.Contains("{" + p + "}", StringComparison.Ordinal)
                    || bare.Contains("{" + p + ",", StringComparison.Ordinal)
                    || bare.Contains("," + p + "}", StringComparison.Ordinal)))
            {
                return false;
            }

            return !DefinesOurMacro.IsMatch(bare);
        }

        private static String StripComments(String source)
        {
            return String.Join('\n', source
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line =>
                {
                    int at = line.IndexOf('%');

                    // a line whose % is escaped is not a comment
                    while (at > 0 && line[at - 1] == '\\')
                    {
                        at = line.IndexOf('%', at + 1);
                    }

                    return at < 0 ? line : line[..at];
                }));
        }

        #endregion

        #region Checking what came back

        private static readonly Regex DefinesOurMacro =
            new(@"\\(newcommand|renewcommand|providecommand|newenvironment)\s*\{?\s*\\?eal",
                RegexOptions.Compiled);

        // A body that redefines a helper is a hard LaTeX error, not a stylistic problem,
        // and a body carrying its own preamble would give us two document classes. Both
        // are worth rejecting and retrying rather than committing.
        internal static String? WhatIsWrongWith(String body)
        {
            if (!body.Contains(@"\begin{document}", StringComparison.Ordinal))
            {
                return @"it has no \begin{document}";
            }

            if (!body.Contains(@"\end{document}", StringComparison.Ordinal))
            {
                return @"it has no \end{document}";
            }

            String beforeBody = body[..body.IndexOf(@"\begin{document}", StringComparison.Ordinal)];

            if (DocumentClassLine.IsMatch(StripComments(beforeBody)))
            {
                return "it carries its own document class, when only the body was asked for";
            }

            if (DefinesOurMacro.IsMatch(StripComments(body)))
            {
                return "it defines one of the eal helpers, which are provided and would clash";
            }

            if (!UsesAnyHelper(body))
            {
                return "it uses none of the eal helpers, so nothing has been translated";
            }

            if (LineBreakWithNoLine(body) is String stray)
            {
                return $"it breaks a line at {stray}, where no line has been started";
            }

            return null;
        }

        // \\ with an optional length, or \newline
        private static readonly Regex LineBreak =
            new(@"\\\\(\s*\[[^\]]*\])?|\\newline\b", RegexOptions.Compiled);

        // the block level helpers, which finish with \par and so leave the sheet between
        // paragraphs rather than in one
        private static readonly String[] EndsAParagraph = { "ealpara", "ealtextblock" };

        // A line break needs a line to break. One written where a paragraph has not
        // started yet - after a blank line, after \par, at the top of an environment, or
        // straight after a helper that ends a paragraph of its own - is
        // "There's no line here to end", and the sheet does not compile.
        //
        // A model reaches for one whenever it wants a gap, and the sheet it is copying
        // gives it the habit, so this is worth catching before the file is committed
        // rather than when somebody tries to print it.
        private static String? LineBreakWithNoLine(String body)
        {
            String bare = StripComments(body);

            foreach (Match match in LineBreak.Matches(bare))
            {
                int at = match.Index - 1;
                int newlines = 0;

                while (at >= 0 && Char.IsWhiteSpace(bare[at]))
                {
                    if (bare[at] == '\n') { newlines++; }
                    at--;
                }

                // nothing before it at all, or a blank line, so no paragraph is open
                if (at < 0 || newlines > 1) { return Quote(bare, match.Index); }

                String before = bare[..(at + 1)];

                if (before.EndsWith(@"\par", StringComparison.Ordinal)
                    || EndsAnEnvironmentOpener(before))
                {
                    return Quote(bare, match.Index);
                }

                if (bare[at] == '}' && ClosesOneOf(before, EndsAParagraph))
                {
                    return Quote(bare, match.Index);
                }
            }

            return null;
        }

        private static bool EndsAnEnvironmentOpener(String before) =>
            Regex.IsMatch(before, @"\\begin\s*\{[^}]*\}$");

        // Walks back from a closing brace over as many argument groups as it finds, to
        // the name of the macro they belong to. That is what tells a break after
        // \ealpara{..}{..} - which is an error - from one after \ealkey{..}, which is an
        // ordinary word in the middle of a line and perfectly fine.
        private static bool ClosesOneOf(String before, IReadOnlyList<String> macros)
        {
            int at = before.Length - 1;

            while (at >= 0 && before[at] == '}')
            {
                int depth = 0;

                for (; at >= 0; at--)
                {
                    if (at > 0 && before[at - 1] == '\\') { continue; }

                    if (before[at] == '}') { depth++; }
                    else if (before[at] == '{' && --depth == 0) { break; }
                }

                if (at < 0) { return false; }

                at--;

                while (at >= 0 && Char.IsWhiteSpace(before[at])) { at--; }
            }

            int end = at + 1;

            while (at >= 0 && Char.IsLetter(before[at])) { at--; }

            return at >= 0 && before[at] == '\\'
                && macros.Contains(before[(at + 1)..end], StringComparer.Ordinal);
        }

        // enough of the surrounding text to find it in the file by eye
        private static String Quote(String bare, int at)
        {
            int from = Math.Max(0, at - 40);

            return "\"..." + bare[from..Math.Min(bare.Length, at + 10)].Replace("\n", " ").Trim() + "...\"";
        }

        private static bool UsesAnyHelper(String body)
        {
            String[] helpers = { @"\ealpara", @"\ealgloss", @"\ealkey", @"\ealkeytr" };

            return helpers.Any(h => body.Contains(h, StringComparison.Ordinal));
        }

        private static readonly Regex Defines = new(
            @"\\(?:new|renew|provide)command\s*\*?\s*\{?\s*\\([A-Za-z]+)"
            + @"|\\def\s*\\([A-Za-z]+)"
            + @"|\\DeclareMathOperator\s*\*?\s*\{?\s*\\([A-Za-z]+)",
            RegexOptions.Compiled);

        private static readonly Regex Uses = new(@"\\([A-Za-z]+)", RegexOptions.Compiled);

        private static IEnumerable<String> MacrosDefinedIn(String tex) =>
            Defines.Matches(StripComments(tex))
                .Select(m => m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value);

        // A macro the sheet defines for itself, which the translation uses and does not
        // define, is an undefined control sequence - the file commits, and then fails to
        // compile in front of whoever tried to print it.
        //
        // Checked against the sheet it came from rather than against LaTeX at large,
        // because that is the question actually worth asking and the only one that can be
        // answered without a TeX engine: nothing here knows whether \frac exists, but it
        // knows perfectly well whether the deck defined \ablank and this file did not.
        internal static String? WhatIsMissingFrom(String assembled, String original)
        {
            HashSet<String> itsOwn = MacrosDefinedIn(PreambleUpToDocument(original)).ToHashSet(StringComparer.Ordinal);

            if (itsOwn.Count == 0) { return null; }

            itsOwn.ExceptWith(MacrosDefinedIn(assembled));

            if (itsOwn.Count == 0) { return null; }

            List<String> used = Uses.Matches(StripComments(assembled))
                .Select(m => m.Groups[1].Value)
                .Where(itsOwn.Contains)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToList();

            if (used.Count == 0) { return null; }

            return "it uses " + String.Join(", ", used.Select(m => "\\" + m))
                + ", which the sheet it was translated from defines and this one does not";
        }

        private static String PreambleUpToDocument(String source)
        {
            String s = source.Replace("\r\n", "\n");

            int at = s.IndexOf(@"\begin{document}", StringComparison.Ordinal);

            return at < 0 ? s : s[..at];
        }

        #endregion
    }
}
