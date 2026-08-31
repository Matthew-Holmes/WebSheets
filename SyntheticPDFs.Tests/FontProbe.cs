using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using System.Text;

namespace SyntheticPDFs.Tests
{
    // Writes latex/test/eal/fontProbe.tex from the shipped configuration.
    //
    // It sits with the tests rather than in the pipeline because nothing at run time
    // wants it. It is a fixture for the content repository's build: it asks the CI
    // container for every font the site can call on, so that a missing one is a single
    // failure with every name in it, rather than a surprise months later when somebody
    // first asks for a sheet in that language.
    internal static class FontProbe
    {
        // One letter of each font's own script, printed to prove the font really loads
        // rather than merely resolving by name.
        //
        // It cannot be a full stop. Several of the Noto script packages carry their
        // script and very little else, so an earlier version of this file that printed
        // "." in every language reported a missing character in five of them and then
        // died outright: a font that is selected and then prints nothing leaves LuaTeX
        // subsetting an empty font, which is a fatal backend error, not a warning.
        private static readonly Dictionary<String, String> Samples = new()
        {
            ["Noto Serif"]           = "A",
            ["Noto Naskh Arabic"]    = "ا", // arabic alef
            ["Noto Nastaliq Urdu"]   = "ا", // arabic alef
            ["Noto Sans Bengali"]    = "অ", // bengali a
            ["Noto Sans CJK JP"]     = "あ", // hiragana a
            ["Noto Sans CJK KR"]     = "가", // hangul ga
            ["Noto Sans CJK SC"]     = "中", // han zhong - not "one", which prints as a dash
            ["Noto Sans Devanagari"] = "अ", // devanagari a
            ["Noto Sans Ethiopic"]   = "ሀ", // ethiopic ha
            ["Noto Sans Gujarati"]   = "અ", // gujarati a
            ["Noto Sans Gurmukhi"]   = "ਅ", // gurmukhi a
            ["Noto Sans Hebrew"]     = "א", // hebrew alef
            ["Noto Sans Kannada"]    = "ಅ", // kannada a
            ["Noto Sans Malayalam"]  = "അ", // malayalam a
            ["Noto Sans Sinhala"]    = "අ", // sinhala ayanna
            ["Noto Sans Tamil"]      = "அ", // tamil a
            ["Noto Sans Telugu"]     = "అ", // telugu a
            ["Noto Sans Thai"]       = "ก", // thai ko kai
        };

        // a font with nothing to print in it is the bug above waiting to happen again,
        // so adding a language in a new script has to stop here first
        internal static IReadOnlyList<String> FontsWithNoSample(L2Options options) =>
            options.Languages.Values
                .Select(language => language.Font)
                .Where(font => !String.IsNullOrWhiteSpace(font) && !Samples.ContainsKey(font))
                .Distinct()
                .OrderBy(font => font, StringComparer.Ordinal)
                .ToList();

        private record Row(String Name, String Code, String Font, String Babel);

        // alphabetical by English name, which is the order the page reads in
        private static List<Row> Rows(L2Options options) =>
            options.Languages
                .Select(entry => new
                {
                    Code  = entry.Key,
                    Name  = LanguageNames.EnglishNameOf(entry.Key),
                    Font  = entry.Value.Font,
                    Babel = entry.Value.BabelName,
                })
                .Where(language => language.Name is not null)
                .Select(language => new Row(
                    Capitalised(language.Name!), language.Code, language.Font, language.Babel))
                .OrderBy(row => row.Name, StringComparer.Ordinal)
                .ToList();

        private static String Capitalised(String name) =>
            name.Length == 0 ? name : Char.ToUpperInvariant(name[0]) + name[1..];

        private static String Faces(String font) =>
            $"Renderer=Harfbuzz, BoldFont={{{font}}}, ItalicFont={{{font}}}, "
            + $"BoldItalicFont={{{font}}}";

        internal static String Render(L2Options options)
        {
            IReadOnlyList<String> unprintable = FontsWithNoSample(options);

            if (unprintable.Count > 0)
            {
                throw new InvalidOperationException(
                    "no sample letter for " + String.Join(", ", unprintable)
                    + " - add one to FontProbe.Samples before configuring a language in it");
            }

            List<Row> rows = Rows(options);

            var sb = new StringBuilder();

            void Line(String text = "") => sb.Append(text).Append('\n');

            Line(Header);
            Line();
            Line(Macros);

            // Farsi and Persian share a babel name, and providing one language twice is
            // not something this file should be the first to find out about
            var provided = new HashSet<String>();

            foreach (Row row in rows)
            {
                if (!provided.Add(row.Babel)) { continue; }

                Line($@"\babelprovide[import]{{{row.Babel}}}");
                Line($@"\IfFontExistsTF{{{row.Font}}}");
                Line($@"  {{\babelfont[{row.Babel}]{{rm}}[{Faces(row.Font)}]{{{row.Font}}}%");
                Line($@"   \babelfont[{row.Babel}]{{sf}}[{Faces(row.Font)}]{{{row.Font}}}%");
                Line($@"   \ealfound{{{row.Babel}}}{{{Samples[row.Font]}}}}}");
                Line($@"  {{\ealabsent{{{row.Name}}}{{{row.Font}}}}}");
            }

            Line();
            Line(Opening);

            foreach (Row row in rows)
            {
                Line($@"{row.Name} ({row.Code}), {row.Font}: \ealshow{{{row.Babel}}}{{{row.Font}}}\\");
            }

            Line();
            Line(Closing);

            return sb.ToString();
        }

        private const String Header = @"% !TeX program = lualatex
% ================================================================
% Font probe
%
% Every language this instance is configured for, and the font it would be set
% in. A font family that is not installed is a hard fontspec error that takes
% the whole build down, and it would otherwise surface one language at a time,
% whenever somebody first asked for a sheet in it.
%
% Nothing here loads a font without asking for it by name first, so one run
% lists every font that is missing rather than stopping at the first. Each is
% written to the log as it is found, and the run then fails with the whole
% list. To have it report without failing the build, change \PackageError at
% the end to \PackageWarning.
%
% What is printed in each language is one letter of its own script, and it has
% to be. Several of the Noto script fonts carry their script and very little
% else. An earlier version of this file printed a full stop in every language,
% which reported a missing character in five of them and then died outright: a
% font that is selected and then prints nothing leaves LuaTeX subsetting an
% empty font, which is a fatal backend error rather than a warning.
%
% That is worth knowing for its own sake, so each font is also asked whether it
% has the Latin characters a translated sheet mixes into its own script. That
% one is reported at the foot of the page and in the log, and is not fatal.
%
% Generated from the generator's configuration by SyntheticPDFs.Tests - see
% docs/content-repo-translation-setup.md before editing it by hand.
% ================================================================

\documentclass[11pt]{article}
\usepackage[a4paper,margin=18mm]{geometry}
\usepackage{xcolor}
\usepackage{fontspec}
\usepackage[english,bidi=basic]{babel}";

        private const String Macros = @"% One line in the log per language as it is found, and each family named once
% in the error at the end - thirty languages set in Noto Serif would otherwise
% put it in the summary thirty times and bury everything else.
\newif\ifealmissing
\def\ealmissinglist{}
\newcommand{\ealabsent}[2]{%
  \global\ealmissingtrue
  \immediate\write17{FONT PROBE: #2 is not installed - needed for #1}%
  \ifcsname ealsaid@#2\endcsname\else
    \expandafter\gdef\csname ealsaid@#2\endcsname{}%
    \xdef\ealmissinglist{\ealmissinglist\space#2;}%
  \fi
}

% Does this font have the Latin characters a translated sheet mixes into its own
% script? A full stop closes most definitions, digits appear in any fraction, and
% the vocabulary key sets an em dash between a word and its meaning. LuaTeX drops
% what a font has not got, leaving a warning in the log and a hole in the page,
% so it is worth learning here rather than from a sheet.
\newif\ifeallatin
\def\eallatinlist{}
\newcommand{\ealnolatin}[2]{%
  \global\eallatintrue
  \ifcsname ealthin@#1@#2\endcsname\else
    \expandafter\gdef\csname ealthin@#1@#2\endcsname{}%
    \immediate\write17{FONT PROBE: #1 has no #2}%
  \fi
  \ifcsname ealthin@#1\endcsname\else
    \expandafter\gdef\csname ealthin@#1\endcsname{}%
    \xdef\eallatinlist{\eallatinlist\space#1;}%
  \fi
}

% asked inside the language, where \font is the font the language would be set in
\newcommand{\eallatin}[1]{%
  \iffontchar\font`\. \else\ealnolatin{#1}{full stops}\fi
  \iffontchar\font`\0 \else\ealnolatin{#1}{digits}\fi
  \iffontchar\font""2014 \else\ealnolatin{#1}{em dashes}\fi
}

% The sample letter is remembered from the one check above, so the body does not
% look a missing font up a second time - every miss costs luaotfload a database
% reload. \selectfont makes the switch happen before \eallatin asks about it.
\newcommand{\ealfound}[2]{\expandafter\gdef\csname ealhas@#1\endcsname{#2}}
\newcommand{\ealshow}[2]{%
  \ifcsname ealhas@#1\endcsname
    \foreignlanguage{#1}{\selectfont\csname ealhas@#1\endcsname\eallatin{#2}}%
  \else
    \textbf{\textcolor{red}{MISSING}}%
  \fi
}
";

        private const String Opening = @"\pagestyle{empty}
\setlength{\parindent}{0pt}

\begin{document}
{\large\bfseries Font probe}\par\medskip
Every language this instance is configured for, with the font it would be set
in. One letter of each language's own script is printed beside it: a font that
is installed but cannot print the script it was chosen for would pass a check
by name and still set a page full of holes.\par\medskip
";

        private const String Closing = @"\ifeallatin
  \par\bigskip
  {\bfseries These fonts carry their own script and little else:}\eallatinlist\par
  \medskip
  Text set in them loses the full stops, digits and em dashes a translated sheet
  mixes into its own script - the characters are dropped rather than substituted,
  leaving holes in the page. Those languages need a Latin fallback configured
  before a sheet in one of them reads properly.
  \PackageWarning{fontProbe}{Fonts with no Latin characters:\eallatinlist}
\fi

\ifealmissing
  \PackageError{fontProbe}{Fonts missing:\ealmissinglist}{%
    Add them to extra_system_packages in the workflow. font-noto-all covers
    every Noto script and cannot go stale as languages are added.%
  }
\fi
\end{document}";
    }
}
