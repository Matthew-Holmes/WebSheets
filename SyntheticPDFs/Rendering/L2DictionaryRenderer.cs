using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models.Content;
using System.Text;

namespace SyntheticPDFs.Rendering
{
    // Writes the shared dictionary in one language.
    //
    // The file is two things at once, and both matter. It compiles to a dictionary a
    // pupil can be handed - the English word, the word their own mathematics teacher
    // would use, and both definitions - and it is the cache the pipeline reads on the
    // next pass, which is why every entry carries the English definition it was
    // translated from.
    //
    // Written here rather than by a model, so that the shape cannot drift and so that a
    // rewrite costs nothing: an entry whose English wording has not changed is copied
    // across, not asked for again.
    internal static class L2DictionaryRenderer
    {
        // bumped when the layout below changes in a way that alters the output. Unlike a
        // sheet, a dictionary is re-rendered from the entries it already holds, so a bump
        // here costs a commit rather than a translation.
        internal const int LayoutVersion = 1;

        internal static String Render(
            L2Dictionary dictionary,
            LanguageProfile language,
            L2ColourOptions colours,
            String builtFrom,
            String? fallbackFont = null)
        {
            String title = $"{language.TitleName} Mathematical Dictionary";

            // by English headword, which is how it is looked up and how a diff stays
            // readable when a word is added in the middle
            List<L2DictionaryEntry> ordered = dictionary.Entries
                .OrderBy(e => e.Headword, StringComparer.Ordinal)
                .ToList();

            StringBuilder sb = new();

            sb.AppendLine(L2Macros.CompilerDirective);
            sb.AppendLine(L2Macros.ProvenanceBlock(
                title, colours, language, builtFrom, vocabularyKey: null,
                isKey: false, fallbackFont: fallbackFont));
            sb.AppendLine(EditingNote(language));
            sb.AppendLine();

            sb.AppendLine(@"\documentclass[11pt]{article}");
            sb.AppendLine(@"\usepackage[a4paper,margin=18mm]{geometry}");
            sb.AppendLine(L2Macros.LanguagePreamble(language, fallbackFont));
            sb.AppendLine(L2Macros.Definitions(colours));
            sb.AppendLine();
            sb.AppendLine(Layout(language.RightToLeft));
            sb.AppendLine();
            sb.AppendLine(@"\pagestyle{empty}");
            sb.AppendLine(@"\setlength{\parindent}{0pt}");
            sb.AppendLine();

            sb.AppendLine(@"\begin{document}");
            sb.AppendLine($@"{{\large\bfseries {Escape(title)}}}\par\medskip");
            sb.AppendLine();

            foreach (L2DictionaryEntry entry in ordered)
            {
                sb.AppendLine(Entry(entry));
            }

            sb.AppendLine();
            sb.AppendLine(@"\end{document}");

            return sb.ToString();
        }

        // One entry per line, so that a diff shows which words changed rather than that
        // the file changed. The English definition is the third thing a reader sees and
        // the second thing the parser reads, which is the one place where what is useful
        // to a person and what is useful to the pipeline happen to agree.
        private static String Entry(L2DictionaryEntry entry) =>
            $@"\dictentrytr{{{Escape(entry.Headword)}}}{{{Escape(entry.English)}}}"
            + $@"{{{Escape(entry.Word)}}}{{{Escape(entry.Definition)}}}";

        // Said in the file itself, because the file is the only place somebody editing it
        // will look. The distinction is real rather than fussiness: the two English
        // arguments are how an entry is matched to the shared dictionary and how the
        // server knows the translation is still current, so an edit to either has effects
        // that are nothing to do with what appears on the page.
        private static String EditingNote(LanguageProfile language) => String.Join('\n',
            "%",
            "% ---- What is safe to edit in this file ----------------------------------",
            "%",
            $"% Safe:     the {language.TitleName} word and the {language.TitleName} "
                + "definition - the third",
            "%           and fourth arguments of each entry. That is what this file is for:",
            "%           if a translation reads wrongly to somebody who speaks the language,",
            "%           correct it here and every sheet that uses the word follows.",
            "%",
            "% Not safe: the English word and the English definition - the first and second",
            "%           arguments. The first is how an entry is matched to the shared",
            "%           dictionary; the second is the wording the translation was made",
            "%           from, and is how the server knows the translation is still current.",
            "%           Changing an English definition here does not change the shared",
            "%           dictionary - it only makes this entry look up to date when it is",
            "%           not. Edit the English in the shared dictionary instead, and this",
            "%           file will be brought back into step on the next run.",
            "%",
            "% Entries are added and removed by the server, following the shared dictionary.",
            "% An entry the server cannot read is reported rather than guessed at, so if a",
            "% run complains about this file, look for a missing brace.",
            "% -------------------------------------------------------------------------");

        // A dictionary is a long list of short rows, so it is set in two columns with the
        // English word hanging in the margin of each entry. Nothing here is shared with
        // the vocabulary keys: restyling one should not rebuild the other.
        private static String Layout(bool rightToLeft) => String.Join('\n',
            "% ================================================================",
            "% Dictionary layout",
            "% ================================================================",
            @"\usepackage{multicol}",
            "",
            @"\newlength{\ealdictgap}",
            @"\setlength{\ealdictgap}{1.2mm}",
            "",
            "% english word / english meaning / translated word / translated meaning",
            @"\newcommand{\dictentrytr}[4]{%",
            @"  \par\addvspace{\ealdictgap}%",
            @"  \noindent",
            @"  {\bfseries\ealkey{#1}}\quad{\small #2}\par",
            @"  \nopagebreak",
            (rightToLeft
                ? @"  {\ealtextblock{{\bfseries\color{ealkeycolour}#3}~\textemdash{} {\small #4}}}%"
                : @"  {\color{ealtwocolour}\ealtext{{\bfseries #3}~\textemdash{} {\small #4}}\par}%"),
            @"  \par",
            @"}");

        private static String Escape(String text) => TexArguments.Escape(text);
    }
}
