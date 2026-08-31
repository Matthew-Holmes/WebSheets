using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
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
            + $"BoldItalicFont={{{font}}}, RawFeature={{fallback=eallatinfallback}}";

        // The fallback every translated file loads, checked here because it is loaded by
        // all of them: a name that does not resolve breaks every translation rather than
        // one, so it is worth failing on rather than reporting.
        private static String Fallback(String font) => String.Join('\n',
            "% Where a character the language's own font has not got is borrowed from.",
            "% Several of the Noto script families carry their script and essentially no",
            "% Latin, and LuaTeX drops what a font has not got rather than substituting one,",
            "% so without this a sheet in one of them loses its punctuation.",
            "%",
            "% Every translated file loads this one font, so a name that does not resolve",
            "% breaks all of them rather than one - which is why it is fatal here.",
            $@"\IfFontExistsTF{{{font}}}{{}}{{%",
            $@"  \PackageError{{fontProbe}}{{The fallback font {font} is not installed}}{{%",
            "    Every translated file loads it, so no translated sheet will keep its full",
            "    stops until it is there. Install it, or change L2:FallbackFont to a family",
            "    that is.%",
            "  }%",
            "}",
            "%",
            "% Naming a fallback that was never registered does not degrade to no",
            "% fallback - it makes the font itself unloadable - so a build whose",
            "% luaotfload is too old to register one has to stop and say so.",
            @"\directlua{%",
            @"  if luaotfload and luaotfload.add_fallback then",
            @"    luaotfload.add_fallback(""eallatinfallback"",",
            $@"      {{ ""{font}:mode=harf;"" }})",
            @"  else",
            @"    tex.error(""this luaotfload is too old to register a fallback font"")",
            @"  end",
            @"}");

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

            // after the macros, since the declaration below calls one of them when this
            // build turns out to be too old to have a fallback at all
            Line(Fallback(options.FallbackFont));
            Line();

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
% Beside the script letter, each language also prints the three Latin characters
% a translated sheet mixes into its own script - a full stop, a digit and an em
% dash. Those come from the fallback font wherever the language's own font has
% not got them, so a Missing character line for one of them means the fallback
% is not working in this container, and every translated sheet is quietly losing
% its punctuation.
%
% Each font is separately asked, silently, whether it has those three of its own.
% That is reported at the foot of the page and in the log and is not fatal: it
% says which languages are relying on the fallback, not that anything is wrong.
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

% Does this font have, of its own, the Latin characters a translated sheet mixes
% into its own script? A full stop closes most definitions, digits appear in any
% fraction, and the key sets an em dash between a word and its meaning.
%
% \iffontchar sees the font itself and not what the fallback lends it, which is
% exactly what is wanted: the fallback fills these holes, and this says which
% languages would have them without it.
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
    \foreignlanguage{#1}{\selectfont
      \csname ealhas@#1\endcsname\eallatin{#2}\space{}. 0 \textemdash}%
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
in. Beside each is one letter of its own script, then a full stop, a digit and
an em dash.\par\medskip
The script letter is there because a font that is installed but cannot print the
script it was chosen for would pass a check by name and still set a page full of
holes. The three after it are there because several of these fonts carry their
script and no Latin at all, and borrow those from a fallback - so a line below
that is missing one of them is a language whose sheets have lost their
punctuation.\par\medskip
";

        private const String Closing = @"\ifeallatin
  \par\bigskip
  {\bfseries These fonts carry their own script and little else:}\eallatinlist\par
  \medskip
  The full stop, digit and em dash printed beside each of them above came from
  the fallback font rather than from the font itself. That is what the fallback
  is for, so this is a note rather than a fault - but if any of those three is
  missing from a line above, or the log carries a Missing character line for one
  of them, then the fallback is not reaching these languages and every translated
  sheet in them is losing its punctuation.
  \PackageWarning{fontProbe}{Fonts with no Latin of their own:\eallatinlist}
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
