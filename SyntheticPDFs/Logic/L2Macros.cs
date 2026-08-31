using SyntheticPDFs.Configuration;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Logic
{
    // The LaTeX every translated file is built from: the compiler directive, the
    // language's own preamble, the layout macros, and the provenance block that records
    // which settings the file was made from.
    //
    // All of it is written here rather than asked of the model. Each piece below was
    // arrived at by compiling - see docs/translated-sheet-latex.md, which records what
    // each rule is protecting against, since most of these fail silently.
    internal static class L2Macros
    {
        // bumped when the macros below change in a way that alters the output, which
        // makes every file built from the old ones stale
        internal const int MacroVersion = 1;

        // The same idea for the layout of a vocabulary key, kept separate because it is
        // only the keys that use it. Restyling the key would otherwise rebuild every
        // parallel text sheet as well, each of which costs an API call to remake and
        // would come back identical.
        internal const int KeyLayoutVersion = 3;

        // Always lualatex, never inherited from the English source. The CI classifier
        // reads this before anything else, so a sheet pinned to pdflatex would take its
        // translation down with it - pdflatex cannot typeset any of these scripts.
        internal static String CompilerDirective => "% !TeX program = lualatex";

        #region The language's own preamble

        // Written from the language profile, never by the model. The four rules encoded
        // here are all silent failures: english as a package option (otherwise babel
        // loads 'nil' and the font never switches), both rm and sf (beamer sets sans,
        // article sets roman), every face named (a font with no bold italic otherwise
        // fails to resolve for the whole family), and nothing touching the sheet's own
        // English font.
        internal static String LanguagePreamble(LanguageProfile language)
        {
            String bidi = language.RightToLeft ? ",bidi=basic" : "";

            String faces = String.Join(", ",
                "Renderer=Harfbuzz",
                $"BoldFont={{{language.Font}}}",
                $"ItalicFont={{{language.Font}}}",
                $"BoldItalicFont={{{language.Font}}}");

            // a right to left language sets flush right; everything else flush left
            String alignment = language.RightToLeft ? @"\raggedleft" : @"\raggedright";

            return String.Join('\n',
                $@"\usepackage[english{bidi}]{{babel}}",
                $@"\babelprovide[import]{{{language.BabelName}}}",
                $@"\babelfont[{language.BabelName}]{{rm}}[{faces}]{{{language.Font}}}",
                $@"\babelfont[{language.BabelName}]{{sf}}[{faces}]{{{language.Font}}}",
                $@"\newcommand{{\ealtext}}[1]{{\foreignlanguage{{{language.BabelName}}}{{#1}}}}",
                $@"\newcommand{{\ealtextblock}}[1]{{{{{alignment}\foreignlanguage{{{language.BabelName}}}{{#1}}\par}}}}");
        }

        #endregion

        #region The layout macros

        // Names are prefixed "eal" so they cannot collide with a macro the sheet already
        // defines, and carry no digit - a TeX control sequence is letters only, so
        // \l2text would parse as \l followed by "2text", silently redefining the barred
        // l that Polish needs.
        internal static String Definitions(L2ColourOptions colours)
        {
            return String.Join('\n',
                "% ================================================================",
                "% EAL layout helpers",
                "% ================================================================",
                @"\usepackage{xcolor}",
                "",
                Colour("ealkeycolour", colours.Tier3),
                Colour("ealenglish", colours.English),
                Colour("ealtwocolour", colours.Translation),
                "",
                @"\newsavebox{\ealboxen}",
                @"\newsavebox{\ealboxtr}",
                @"\newlength{\ealglosswd}",
                @"\newlength{\ealglossraise}",
                @"\setlength{\ealglossraise}{1.15em}",
                "",
                "% a tier 3 word, in English and in translation alike",
                @"\newcommand{\ealkey}[1]{\textcolor{ealkeycolour}{#1}}",
                @"\newcommand{\ealkeytr}[1]{\textcolor{ealkeycolour}{\ealtext{#1}}}",
                "",
                "% One translated word above one English word. Built from kernel",
                "% primitives, never a tabular, which would break wherever the sheet",
                "% loads array, booktabs or tabularx. The box is as wide as the wider of",
                "% the two, so a long translation pushes its neighbours aside instead of",
                "% overlapping them, and it claims its full height so a table row or a",
                "% TikZ node makes room for it.",
                @"\newcommand{\ealgloss}[2]{%",
                @"  \sbox{\ealboxen}{\ealkey{#1}}%",
                @"  \sbox{\ealboxtr}{\small\textcolor{ealtwocolour}{\ealtext{#2}}}%",
                @"  \setlength{\ealglosswd}{\wd\ealboxen}%",
                @"  \ifdim\wd\ealboxtr>\ealglosswd\setlength{\ealglosswd}{\wd\ealboxtr}\fi",
                @"  \leavevmode",
                @"  \makebox[\ealglosswd][c]{%",
                @"    \makebox[0pt][c]{\usebox{\ealboxen}}%",
                @"    \makebox[0pt][c]{\raisebox{\ealglossraise}{\usebox{\ealboxtr}}}%",
                @"  }%",
                @"}",
                "",
                "% opens up line spacing around a block of glosses, so the raised",
                "% translations sit clear of the line above",
                @"\newenvironment{ealglossed}{\par\linespread{2.1}\selectfont\ignorespaces}{\par}",
                "",
                "% a whole translated sentence above its English counterpart. block level,",
                "% so it does not belong inside a tabular cell",
                @"\newcommand{\ealpara}[2]{%",
                @"  \par\addvspace{0.5\baselineskip}%",
                @"  {\color{ealtwocolour}\ealtextblock{#1}}%",
                @"  \penalty200",
                @"  {\color{ealenglish}#2\par}%",
                @"  \addvspace{0.5\baselineskip}%",
                @"}");
        }

        private static String Colour(String name, RgbColourOptions colour) =>
            $@"\definecolor{{{name}}}{{RGB}}{{{colour.R},{colour.G},{colour.B}}}";

        // the cheap necessary condition that a generated file is actually usable,
        // checked before it is committed
        internal static bool AreDefined(String texSource)
        {
            String normalised = Normalise(texSource);

            return normalised.Contains(@"\newcommand{\ealkey}", StringComparison.Ordinal)
                && normalised.Contains(@"\newcommand{\ealgloss}", StringComparison.Ordinal)
                && normalised.Contains(@"\newcommand{\ealpara}", StringComparison.Ordinal)
                && normalised.Contains(@"\newcommand{\ealtext}", StringComparison.Ordinal);
        }

        internal static bool HasCompilerDirective(String texSource) =>
            Normalise(texSource)
                .Split('\n')
                .Take(5)
                .Any(line => line.Trim() == CompilerDirective);

        #endregion

        #region Provenance

        private const String BlockRule =
            "% ================================================================";

        // What the file was made from, in plain words and plain numbers. Written by the
        // generator rather than the model, so it cannot be paraphrased away, and read
        // back by ParseProvenance to decide whether the settings have moved since.
        //
        // Deliberately not a hash: there are only three colours and a version number, so
        // writing them out is both more useful to whoever opens the file and exactly as
        // comparable.
        internal static String ProvenanceBlock(
            String title,
            L2ColourOptions colours,
            LanguageProfile? language,
            String builtFrom,
            String? vocabularyKey,
            bool isKey = false)
        {
            List<String> lines = new()
            {
                BlockRule,
                "% " + title,
                "%",
                "% Generated by the server using the following settings:",
                "%",
            };

            if (language is not null)
            {
                lines.Add(Setting("language", $"{language.TitleName} ({language.Code.Code})"));
                lines.Add(Setting("text direction", language.DirectionDescription));
                lines.Add(Setting("font", language.Font));
            }

            lines.Add(Setting("built from", builtFrom));

            if (vocabularyKey is not null)
            {
                lines.Add(Setting("vocabulary key", vocabularyKey));
            }

            lines.Add(Setting("tier 3 vocabulary", Describe(colours.Tier3)));
            lines.Add(Setting("English text", Describe(colours.English)));

            String translationLabel = language is null
                ? "translated text"
                : $"{language.TitleName} text";

            lines.Add(Setting(translationLabel, Describe(colours.Translation)));
            lines.Add(Setting("layout macros", $"version {MacroVersion}"));

            if (isKey)
            {
                lines.Add(Setting("key layout", $"version {KeyLayoutVersion}"));
            }

            lines.AddRange(new[]
            {
                "%",
                "% Please don't edit any information in this comment block - the server",
                "% compares it against the current settings and rebuilds this file when",
                "% they differ, so changes here would be overwritten. However, feel free",
                "% to make updates to the LaTeX below if you want to edit it.",
                BlockRule,
            });

            return String.Join('\n', lines);
        }

        private static String Setting(String name, String value) =>
            "%   " + name.PadRight(20) + value;

        private static String Describe(RgbColourOptions colour) =>
            $"{colour.Name.PadRight(8)}RGB {colour.R} {colour.G} {colour.B}";

        // "Polish Parallel Text Version of AlgebraStarters01" - named for whoever opens
        // the file, not for the pipeline
        internal static String TitleFor(
            SourceMetadataTitle metadata, LanguageProfile? language)
        {
            String sheet = Capitalise(metadata.RootName.Split('/').Last());

            String what = metadata.Rendition switch
            {
                SourceRendition.VocabKey     => "Tier 3 Vocabulary Key",
                SourceRendition.L2Key        => "Tier 3 Vocabulary Key",
                SourceRendition.ParallelText => "Parallel Text Version",
                SourceRendition.Tier3Only    => "Tier 3 Vocabulary Version",
                _ => "Version",
            };

            String of = metadata.Type switch
            {
                SourceType.WorkedSolutions => $"the Worked Solutions for {sheet}",
                SourceType.Solutions       => $"the Answers for {sheet}",
                _                          => sheet,
            };

            String prefix = language is null ? "" : language.TitleName + " ";

            return metadata.Rendition is SourceRendition.VocabKey or SourceRendition.L2Key
                ? $"{prefix}{what} for {of}"
                : $"{prefix}{what} of {of}";
        }

        // just the parts of the metadata a title needs, so the renderer does not have to
        // reach into the orchestrator's types
        internal readonly record struct SourceMetadataTitle(
            String RootName, SourceType Type, SourceRendition Rendition);

        private static String Capitalise(String name) =>
            name.Length == 0 ? name : Char.ToUpperInvariant(name[0]) + name[1..];

        // ---- reading it back ----

        internal record Provenance
        {
            internal required IReadOnlyList<(int R, int G, int B)> Colours { get; init; }
            internal required int MacroVersion { get; init; }

            // null in anything that is not a vocabulary key, and in a key written
            // before the layout was versioned
            internal int? KeyLayoutVersion { get; init; }
        }

        private static readonly Regex RgbLine =
            new(@"RGB\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.Compiled);

        private static readonly Regex VersionLine =
            new(@"layout macros\s+version\s+(\d+)", RegexOptions.Compiled);

        private static readonly Regex KeyLayoutLine =
            new(@"key layout\s+version\s+(\d+)", RegexOptions.Compiled);

        // null when the file carries no block we can read, which counts as out of date -
        // a file with no record of what it was made from cannot be shown to be current
        internal static Provenance? ParseProvenance(String texSource)
        {
            String normalised = Normalise(texSource);

            var version = VersionLine.Match(normalised);

            if (!version.Success) { return null; }

            var colours = RgbLine.Matches(normalised)
                .Select(m => (
                    R: int.Parse(m.Groups[1].Value),
                    G: int.Parse(m.Groups[2].Value),
                    B: int.Parse(m.Groups[3].Value)))
                .ToList();

            if (colours.Count < 3) { return null; }

            var keyLayout = KeyLayoutLine.Match(normalised);

            return new Provenance
            {
                Colours          = colours,
                MacroVersion     = int.Parse(version.Groups[1].Value),
                KeyLayoutVersion = keyLayout.Success
                    ? int.Parse(keyLayout.Groups[1].Value)
                    : null,
            };
        }

        // whether a file already in the repository was built from the settings in force
        // now. this is what makes a settings change rebuild only what it actually affects
        internal static bool MatchesSettings(
            String texSource, L2ColourOptions colours, bool isKey = false)
        {
            Provenance? provenance = ParseProvenance(texSource);

            if (provenance is null) { return false; }

            if (provenance.MacroVersion != MacroVersion) { return false; }

            // a key written before the layout was versioned records nothing, which
            // cannot be shown to be current and so is not
            if (isKey && provenance.KeyLayoutVersion != KeyLayoutVersion) { return false; }

            var wanted = new[]
            {
                (colours.Tier3.R, colours.Tier3.G, colours.Tier3.B),
                (colours.English.R, colours.English.G, colours.English.B),
                (colours.Translation.R, colours.Translation.G, colours.Translation.B),
            };

            return wanted.All(w => provenance.Colours.Contains(w));
        }

        #endregion

        private static String Normalise(String texSource) =>
            texSource.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
