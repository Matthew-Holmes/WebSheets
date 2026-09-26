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
            String? fallbackFont = null,

            // whether the archetype this came from asked for anything of its own, which
            // the block records so that a change to what it asks for is a rebuild
            bool sheetLayout = false)
        {
            StringBuilder sb = new();

            sb.AppendLine(L2Macros.CompilerDirective);
            sb.AppendLine(L2Macros.ProvenanceBlock(
                title, colours, language, builtFrom, vocabularyKey,
                isKey: false, fallbackFont: fallbackFont, sheetLayout: sheetLayout));
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

        // Everything from \begin{document} on, which is all a model is asked to translate.
        //
        // It used to be sent the whole file. It was shown the sheet's own macros, copied
        // them after \begin{document}, and then the preamble was put back around what it
        // wrote - so four translations defined \ablank and friends twice and did not
        // compile. It cannot repeat what it was never shown.
        internal static String BodyOf(String source)
        {
            List<String> lines = source.Replace("\r\n", "\n").Split('\n').ToList();

            int start = lines.FindIndex(l =>
                StripComments(l).Contains(@"\begin{document}", StringComparison.Ordinal));

            // no body to speak of, so it sees what there is rather than nothing
            if (start < 0) { return source; }

            lines[start] = lines[start][lines[start].IndexOf(@"\begin{document}", StringComparison.Ordinal)..];

            return String.Join('\n', lines.Skip(start));
        }

        // What the sheet's preamble defines, named the way a prompt can list them. The
        // model still needs to know these exist - a deck's body is full of \ablank - just
        // not how they are written.
        internal static IReadOnlyList<String> MacrosDefinedBy(String preamble) =>
            DefinitionsIn(preamble)
                .Select(d => d.Display)
                .Distinct(StringComparer.Ordinal)
                .ToList();

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
        //
        // Given the English file it was translated from, it is also checked against that:
        // for a macro the preamble defines being defined again, and for maths that has
        // lost its dollars where the English had them.
        internal static String? WhatIsWrongWith(String body, String? original = null)
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

            if (original is not null && DefinedAgain(body, PreambleOf(original)) is String again)
            {
                return $"it defines {again}, which the sheet's own preamble already defines - "
                    + "defining it a second time stops the file compiling";
            }

            if (!UsesAnyHelper(body))
            {
                return "it uses none of the eal helpers, so nothing has been translated";
            }

            if (HelperThatDoesNotExist(body) is String invented)
            {
                return $"it uses {invented}, which is not one of the helpers - the only ones are "
                    + @"\ealkey, \ealkeytr, \ealgloss, \ealpara, \ealtext, \ealtextblock and the "
                    + "ealglossed environment";
            }

            if (LineBreakWithNoLine(body) is String stray)
            {
                return $"it breaks a line at {stray}, where no line has been started";
            }

            // Only where the English reads cleanly. A sheet with a macro of its own that
            // opens maths for its arguments - \sqfrac{\sqrt 2}{3} - puts \sqrt outside any
            // dollars quite legitimately, and nothing short of expanding the macro could
            // tell. A sheet like that is not checked for this at all, rather than failed
            // for something its English does too.
            if ((original is null || MathsOutsideMaths(BodyOf(original)) is null)
                && MathsOutsideMaths(body) is String loose)
            {
                return $"it uses {loose}, where no maths has been opened - it needs $ around it, "
                    + "as it had in the English";
            }

            return null;
        }

        #region What a body defines

        private enum DefinitionKind
        {
            // \newcommand and its like, which stop the file if the name is already taken
            New,

            // \renewcommand, \providecommand and \def, which do not
            Other,
        }

        private sealed record MacroDefinition(String Name, bool IsEnvironment, DefinitionKind Kind)
        {
            internal String Display => IsEnvironment ? $"the {Name} environment" : "\\" + Name;
        }

        // Every definition in some tex, in order. .NET lets alternatives share a group
        // name, so each reads the same way whichever of them matched.
        private static readonly Regex Definition = new(
            @"\\(?<kind>(?:new|renew|provide)command|(?:New|Renew|Provide|Declare)DocumentCommand|DeclareMathOperator)\s*\*?\s*\{?\s*\\(?<name>[A-Za-z]+)"
            + @"|\\(?<kind>(?:new|renew)environment|(?:New|Renew|Provide|Declare)DocumentEnvironment)\s*\*?\s*\{(?<env>[A-Za-z*]+)\}"
            + @"|\\(?<kind>[gex]?def)\s*\\(?<name>[A-Za-z]+)",
            RegexOptions.Compiled);

        private static readonly HashSet<String> FailsIfTaken = new(StringComparer.Ordinal)
        {
            "newcommand", "NewDocumentCommand", "DeclareMathOperator",
            "newenvironment", "NewDocumentEnvironment",
        };

        private static IEnumerable<MacroDefinition> DefinitionsIn(String tex) =>
            Definition.Matches(StripComments(tex)).Select(m =>
            {
                bool isEnvironment = m.Groups["env"].Success;

                return new MacroDefinition(
                    isEnvironment ? m.Groups["env"].Value : m.Groups["name"].Value,
                    isEnvironment,
                    FailsIfTaken.Contains(m.Groups["kind"].Value) ? DefinitionKind.New : DefinitionKind.Other);
            });

        // the names some tex defines for itself, of whatever kind
        internal static IReadOnlySet<String> NamesDefinedIn(String tex) =>
            DefinitionsIn(tex).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);

        // A \newcommand for a name that is already taken is "Command \x already defined",
        // and the file stops there. Taken means by the preamble the body is about to be
        // put under, or earlier in the body itself. An environment and a command share
        // a name - \newenvironment{x} defines \x - so they are checked together.
        private static String? DefinedAgain(String body, String preamble)
        {
            HashSet<String> taken = NamesDefinedIn(preamble).ToHashSet(StringComparer.Ordinal);

            foreach (MacroDefinition definition in DefinitionsIn(body))
            {
                if (definition.Kind == DefinitionKind.New && taken.Contains(definition.Name))
                {
                    return definition.Display;
                }

                taken.Add(definition.Name);
            }

            return null;
        }

        #endregion

        // Any eal name the helpers do not define. A model that shortens \ealgloss to
        // \ealgl writes something that reads as right and is an undefined control sequence.
        private static readonly Regex EalName = new(
            @"\\(?<cs>eal[A-Za-z]*)|\\(?:begin|end)\s*\{(?<env>eal[A-Za-z]*)\}",
            RegexOptions.Compiled);

        private static String? HelperThatDoesNotExist(String body)
        {
            foreach (Match match in EalName.Matches(StripComments(body)))
            {
                bool isEnvironment = match.Groups["env"].Success;

                String name = isEnvironment ? match.Groups["env"].Value : match.Groups["cs"].Value;

                IReadOnlySet<String> defined = isEnvironment ? L2Macros.HelperEnvironments : L2Macros.HelperNames;

                if (!defined.Contains(name))
                {
                    return isEnvironment ? $"an environment called {name}" : "\\" + name;
                }
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
                if (!ALineIsOpenAt(bare, match.Index)) { return Quote(bare, match.Index); }
            }

            return null;
        }

        // Walks back from a line break to whatever settles whether a paragraph is open.
        //
        // Vertical space settles nothing either way - "\ealpara{..}{..} \vspace{1em}\\"
        // is as broken as the same without the \vspace, and "words \vspace{1em}\\" is as
        // fine - so it is stepped over and the walk carries on behind it.
        private static bool ALineIsOpenAt(String bare, int index)
        {
            int at = index - 1;

            while (true)
            {
                int newlines = 0;

                while (at >= 0 && Char.IsWhiteSpace(bare[at]))
                {
                    if (bare[at] == '\n') { newlines++; }
                    at--;
                }

                // nothing before it at all, or a blank line, so no paragraph is open
                if (at < 0 || newlines > 1) { return false; }

                String before = bare[..(at + 1)];

                if (EndsAParagraphCommand.IsMatch(before)
                    || EndsAnEnvironmentOpener(before)
                    || ClosesAParagraphEndingEnvironment.IsMatch(before))
                {
                    return false;
                }

                if (bare[at] == '}' && ClosesOneOf(before, EndsAParagraph)) { return false; }

                Match space = VerticalSpace.Match(before);

                if (!space.Success) { return true; }

                at = space.Index - 1;
            }
        }

        private static bool EndsAnEnvironmentOpener(String before) =>
            Regex.IsMatch(before, @"\\begin\s*\{[^}]*\}$");

        // commands that end the paragraph they are in, so nothing is left to break
        private static readonly Regex EndsAParagraphCommand = new(
            @"\\(?:par|newpage|clearpage)$", RegexOptions.Compiled | RegexOptions.RightToLeft);

        // Environments that close with \par, and so leave the page between paragraphs -
        // "\end{ealglossed}\\[0.4em]" is the one that reached the repository. A tabular,
        // a minipage or a tikzpicture is a box sitting in a line, and is not one of these.
        private static readonly Regex ClosesAParagraphEndingEnvironment = new(
            @"\\end\s*\{(?:ealglossed|itemize|enumerate|description|center|flushleft|flushright|quote|quotation|verse)\}$",
            RegexOptions.Compiled | RegexOptions.RightToLeft);

        // vertical space, which is stepped over rather than read as starting a line
        private static readonly Regex VerticalSpace = new(
            @"\\(?:vspace\*?\s*\{[^{}]*\}|(?:small|med|big)skip)$",
            RegexOptions.Compiled | RegexOptions.RightToLeft);

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

        #region Maths that has lost its dollars

        // commands that exist only in maths - in running text each is "Missing $ inserted"
        private static readonly HashSet<String> MathsOnly = new(StringComparer.Ordinal)
        {
            "frac", "dfrac", "tfrac", "sqrt", "times", "div", "pm", "cdot", "cdots",
            "le", "leq", "ge", "geq", "neq", "approx", "infty", "circ", "angle",
            "displaystyle", "overline", "left", "right", "sum",
            "alpha", "beta", "gamma", "delta", "theta", "lambda", "mu", "pi", "sigma", "phi", "omega",
        };

        // environments that are maths from beginning to end
        private static readonly HashSet<String> MathsEnvironments = new(StringComparer.Ordinal)
        {
            "equation", "equation*", "align", "align*", "alignat", "alignat*",
            "flalign", "flalign*", "gather", "gather*", "multline", "multline*",
            "eqnarray", "eqnarray*", "displaymath", "math",
        };

        // The first maths-only command written where no maths is open - a \sqrt or a
        // \frac that has lost the $ around it, which is what two translations did:
        // \ablank{$F=10+2\sqrt{13}$} came back as \ablank{F=10+2\sqrt{13}}.
        //
        // One pass, reading a backslash together with what follows it so that \$ and \\
        // are never taken for delimiters, and skipping comments. An inline span cannot
        // cross a blank line, so one left open by mistake stops there rather than hiding
        // everything after it.
        private static String? MathsOutsideMaths(String tex)
        {
            bool inline = false;
            bool display = false;
            int environments = 0;

            int i = 0;

            while (i < tex.Length)
            {
                char c = tex[i];

                if (c == '%')
                {
                    // stop on the newline rather than past it, so a blank line after a
                    // comment is still seen
                    int newline = tex.IndexOf('\n', i);
                    i = newline < 0 ? tex.Length : newline;
                    continue;
                }

                if (c == '$')
                {
                    if (i + 1 < tex.Length && tex[i + 1] == '$') { display = !display; i += 2; }
                    else { inline = !inline; i++; }

                    continue;
                }

                if (c == '\n' && StartsABlankLine(tex, i)) { inline = false; }

                if (c != '\\' || i + 1 >= tex.Length) { i++; continue; }

                char next = tex[i + 1];

                if (!Char.IsLetter(next))
                {
                    if (next == '(') { inline = true; }
                    else if (next == ')') { inline = false; }
                    else if (next == '[') { display = true; }
                    else if (next == ']') { display = false; }

                    i += 2;
                    continue;
                }

                int end = i + 1;

                while (end < tex.Length && Char.IsLetter(tex[end])) { end++; }

                String name = tex[(i + 1)..end];

                if (name is "begin" or "end")
                {
                    if (EnvironmentNamedAt(tex, end) is String environment
                        && MathsEnvironments.Contains(environment))
                    {
                        environments += name == "begin" ? 1 : -1;
                    }
                }
                else if (MathsOnly.Contains(name) && !inline && !display && environments <= 0)
                {
                    return "\\" + name + " at " + Quote(tex, i);
                }

                i = end;
            }

            return null;
        }

        private static bool StartsABlankLine(String tex, int newline)
        {
            int k = newline + 1;

            while (k < tex.Length && (tex[k] == ' ' || tex[k] == '\t' || tex[k] == '\r')) { k++; }

            return k < tex.Length && tex[k] == '\n';
        }

        // the name in "{name}" just after \begin or \end
        private static String? EnvironmentNamedAt(String tex, int at)
        {
            while (at < tex.Length && Char.IsWhiteSpace(tex[at])) { at++; }

            if (at >= tex.Length || tex[at] != '{') { return null; }

            int close = tex.IndexOf('}', at);

            return close < 0 ? null : tex[(at + 1)..close].Trim();
        }

        #endregion

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
