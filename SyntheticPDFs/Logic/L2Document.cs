using SyntheticPDFs.Configuration;
using System.Text;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Logic
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
            IReadOnlyList<String> packages,
            String title,
            L2ColourOptions colours,
            LanguageProfile language,
            String builtFrom,
            String vocabularyKey)
        {
            StringBuilder sb = new();

            sb.AppendLine(L2Macros.CompilerDirective);
            sb.AppendLine(L2Macros.ProvenanceBlock(
                title, colours, language, builtFrom, vocabularyKey));
            sb.AppendLine();

            sb.AppendLine(documentClass);

            foreach (String package in packages)
            {
                sb.AppendLine(package);
            }

            sb.AppendLine(L2Macros.LanguagePreamble(language));
            sb.AppendLine(L2Macros.Definitions(colours));
            sb.AppendLine();
            sb.Append(body);

            return sb.ToString();
        }

        #region Reading the original's preamble

        private static readonly Regex DocumentClassLine =
            new(@"^[ \t]*\\documentclass.*$", RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex PackageLine =
            new(@"^[ \t]*\\(usepackage|usetikzlibrary|definecolor|newtheorem)\b.*$",
                RegexOptions.Multiline | RegexOptions.Compiled);

        // The translated sheet keeps the original's class and packages, so a deck stays a
        // deck and a sheet that draws diagrams can still draw them. Read from the English
        // source rather than asked of the model, for the same reason as the rest of the
        // preamble.
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

        internal static IReadOnlyList<String> PackagesOf(String source)
        {
            return PackageLine.Matches(StripComments(source))
                .Select(m => m.Value.Trim())
                .Where(line => !OursAlready.Any(p =>
                    line.Contains("{" + p + "}", StringComparison.Ordinal)
                    || line.Contains("{" + p + ",", StringComparison.Ordinal)
                    || line.Contains("," + p + "}", StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal)
                .ToList();
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

            return null;
        }

        private static bool UsesAnyHelper(String body)
        {
            String[] helpers = { @"\ealpara", @"\ealgloss", @"\ealkey", @"\ealkeytr" };

            return helpers.Any(h => body.Contains(h, StringComparison.Ordinal));
        }

        #endregion
    }
}
